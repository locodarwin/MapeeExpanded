"""Builds LegacyBlocks.json: the Java state for every block state and numeric ID older Bedrock worlds contain.

Bedrock only upgrades a chunk's blocks when the game loads that chunk, so a world keeps whatever format each
chunk was last saved in: numeric IDs and data values before 1.2.13, name + data value palettes until 1.13,
then block states whose names and properties keep changing (wool split by colour in 1.19.70, and so on).

pmmp's BedrockBlockUpgradeSchema (CC0) describes every one of those changes as JSON, and
BedrockBlockPaletteArchive (CC0) holds every state each version could save. Running each archived state and
each legacy ID/meta through the schemas, up to the version Blocks.json describes, gives a modern Bedrock state
that Blocks.json translates. Doing it here keeps the app to plain table lookups.

The upgrade follows PocketMine-MP 5.21's BlockStateUpgrader.
"""
import json
import struct
import urllib.request

SCHEMA_REPO = "pmmp/BedrockBlockUpgradeSchema"
PALETTE_REPO = "pmmp/BedrockBlockPaletteArchive"
RAW = "https://raw.githubusercontent.com/{repo}/master/{path}"
CONTENTS = "https://api.github.com/repos/{repo}/contents/{path}"

TAG_BYTE, TAG_SHORT, TAG_INT, TAG_LONG, TAG_FLOAT, TAG_DOUBLE = 1, 2, 3, 4, 5, 6
TAG_BYTE_ARRAY, TAG_STRING, TAG_LIST, TAG_COMPOUND, TAG_INT_ARRAY, TAG_LONG_ARRAY = 7, 8, 9, 10, 11, 12
JSON_TAGS = {"byte": TAG_BYTE, "int": TAG_INT, "string": TAG_STRING}


def fetch_bytes(url):
    with urllib.request.urlopen(url) as response:
        return response.read()


def list_files(repo, path=""):
    return [entry["name"] for entry in json.loads(fetch_bytes(CONTENTS.format(repo=repo, path=path))) if entry["type"] == "file"]


def version_id(major, minor, patch, revision):
    return (major << 24) | (minor << 16) | (patch << 8) | revision


def version_of(name):
    """'1.19.70.26_beta' -> (1, 19, 70, 26)"""
    parts = [int(p) for p in name.removesuffix(".nbt").split("_")[0].split(".")]
    return tuple((parts + [0, 0, 0, 0])[:4])


class NbtReader:
    """Little-endian NBT (network=False), or the varint form the palette archive uses (network=True).
    Values come back as (tag type, value) so a byte 1 and an int 1 stay different, as the schemas require."""

    def __init__(self, data, network):
        self.data = data
        self.pos = 0
        self.network = network

    def eof(self):
        return self.pos >= len(self.data)

    def unpack(self, fmt):
        value = struct.unpack_from("<" + fmt, self.data, self.pos)
        self.pos += struct.calcsize("<" + fmt)
        return value[0]

    def uvarint(self):
        result = shift = 0
        while True:
            b = self.data[self.pos]
            self.pos += 1
            result |= (b & 0x7F) << shift
            if not b & 0x80:
                return result
            shift += 7

    def zigzag(self):
        n = self.uvarint()
        return (n >> 1) ^ -(n & 1)

    def string(self):
        length = self.uvarint() if self.network else self.unpack("H")
        value = self.data[self.pos:self.pos + length].decode("utf-8")
        self.pos += length
        return value

    def int(self):
        return self.zigzag() if self.network else self.unpack("i")

    def root(self):
        tag = self.unpack("B")
        self.string()
        return self.payload(tag)

    def payload(self, tag):
        if tag == TAG_BYTE: return self.unpack("b")
        if tag == TAG_SHORT: return self.unpack("h")
        if tag == TAG_INT: return self.int()
        if tag == TAG_LONG: return self.zigzag() if self.network else self.unpack("q")
        if tag == TAG_FLOAT: return self.unpack("f")
        if tag == TAG_DOUBLE: return self.unpack("d")
        if tag == TAG_STRING: return self.string()
        if tag == TAG_BYTE_ARRAY:
            n = self.int()
            value = self.data[self.pos:self.pos + n]
            self.pos += n
            return value
        if tag == TAG_INT_ARRAY: return [self.int() for _ in range(self.int())]
        if tag == TAG_LONG_ARRAY: return [self.payload(TAG_LONG) for _ in range(self.int())]
        if tag == TAG_LIST:
            item = self.unpack("B")
            return [self.payload(item) for _ in range(self.int())]
        if tag == TAG_COMPOUND:
            output = {}
            while True:
                child = self.unpack("B")
                if child == 0:
                    return output
                name = self.string()
                output[name] = (child, self.payload(child))
        raise ValueError(f"Unknown NBT tag {tag} at {self.pos}")


class BlockState:
    def __init__(self, name, states, version):
        self.name = name
        self.states = states  # property -> (tag type, value)
        self.version = version

    @staticmethod
    def from_nbt(compound):
        states = compound.get("states", (TAG_COMPOUND, {}))[1]
        return BlockState(compound["name"][1], dict(states), compound.get("version", (TAG_INT, 0))[1])

    def key(self):
        """The key Blocks.json uses and BedrockBlockTranslator builds: name[a=1,b=x], properties sorted by name"""
        return f"{self.name}[{','.join(f'{k}={v[1]}' for k, v in sorted(self.states.items()))}]"


def json_tag(value):
    ((kind, raw),) = value.items()
    return (JSON_TAGS[kind], raw)


class FlattenRule:
    def __init__(self, model):
        self.prefix = model["prefix"]
        self.property = model["flattenedProperty"]
        self.type = JSON_TAGS[model.get("flattenedPropertyType") or "string"]
        self.suffix = model["suffix"]
        self.remaps = model.get("flattenedValueRemaps", {})

    def apply(self, name, states):
        value = states.get(self.property)
        if value is None or value[0] != self.type:
            return name, states
        embed = str(value[1])
        states = dict(states)
        del states[self.property]
        return f"{self.prefix}{self.remaps.get(embed, embed)}{self.suffix}", states


class Schema:
    def __init__(self, schema_id, model):
        self.schema_id = schema_id
        self.version = version_id(model["maxVersionMajor"], model["maxVersionMinor"], model["maxVersionPatch"], model["maxVersionRevision"])
        self.renamed_ids = model.get("renamedIds", {})
        self.renamed_properties = model.get("renamedProperties", {})
        self.removed_properties = model.get("removedProperties", {})
        self.added_properties = {b: {p: json_tag(v) for p, v in props.items()} for b, props in model.get("addedProperties", {}).items()}

        index = {k: [(json_tag(p["old"]), json_tag(p["new"])) for p in pairs] for k, pairs in model.get("remappedPropertyValuesIndex", {}).items()}
        self.remapped_values = {b: {p: index[key] for p, key in props.items()} for b, props in model.get("remappedPropertyValues", {}).items()}
        self.flattened = {b: FlattenRule(rule) for b, rule in model.get("flattenedProperties", {}).items()}

        self.remapped_states = {}
        for block, remaps in model.get("remappedStates", {}).items():
            self.remapped_states[block] = [(
                {p: json_tag(v) for p, v in (remap.get("oldState") or {}).items()},
                remap["newName"] if "newName" in remap else FlattenRule(remap["newFlattenedName"]),
                {p: json_tag(v) for p, v in (remap.get("newState") or {}).items()},
                remap.get("copiedState", []),
            ) for remap in remaps]

    def new_value(self, block, prop, value):
        for old, new in self.remapped_values.get(block, {}).get(prop, []):
            if old == value:
                return new
        return value

    def apply(self, state):
        for old_state, new_name, new_state, copied in self.remapped_states.get(state.name, []):
            if len(old_state) > len(state.states) or any(state.states.get(k) != v for k, v in old_state.items()):
                continue
            if isinstance(new_name, FlattenRule):
                new_name, _ = new_name.apply(state.name, state.states)
            states = dict(new_state)
            for prop in copied:
                if prop in state.states:
                    states[prop] = state.states[prop]
            return BlockState(new_name, states, self.version)

        old = state.name
        states = dict(state.states)
        new_name = None
        if old in self.renamed_ids:
            new_name = self.renamed_ids[old]
        elif old in self.flattened:
            new_name, states = self.flattened[old].apply(old, states)

        changes = 0
        for prop, value in self.added_properties.get(old, {}).items():
            if prop not in states:
                states[prop] = value
                changes += 1
        for prop in self.removed_properties.get(old, []):
            if prop in states:
                del states[prop]
                changes += 1
        for prop, renamed in self.renamed_properties.get(old, {}).items():
            if prop in states:
                value = states.pop(prop)
                states[renamed] = self.new_value(old, prop, value)
                changes += 1
        for prop in self.remapped_values.get(old, {}):
            if prop in states:
                value = self.new_value(old, prop, states[prop])
                if value != states[prop]:
                    states[prop] = value
                    changes += 1

        if new_name is not None or changes > 0:
            return BlockState(new_name or old, states, self.version)
        return state


class Upgrader:
    def __init__(self, schemas):
        self.groups = {}
        for schema in schemas:
            self.groups.setdefault(schema.version, []).append(schema)
        for group in self.groups.values():
            group.sort(key=lambda s: s.schema_id)

    def upgrade(self, state):
        for result_version in sorted(self.groups):
            group = self.groups[result_version]
            # One schema per version is skipped when the state already has that version; several can't be,
            # since Mojang sometimes changed blocks without bumping it
            if state.version > result_version or (len(group) == 1 and state.version == result_version):
                continue
            for schema in group:
                state = schema.apply(state)
        return state


def load_schemas(max_version):
    schemas = []
    for name in list_files(SCHEMA_REPO, "nbt_upgrade_schema"):
        if not name.endswith(".json"):
            continue
        schema = Schema(int(name.split("_")[0]), json.loads(fetch_bytes(RAW.format(repo=SCHEMA_REPO, path=f"nbt_upgrade_schema/{name}"))))
        if schema.version <= max_version:
            schemas.append(schema)
    return schemas


def read_id_meta(data):
    """id_meta_to_nbt/*.bin: varint count of string IDs, each with a varint count of (varint meta, LE NBT state)"""
    reader = NbtReader(data, network=False)
    output = {}
    for _ in range(reader.uvarint()):
        length = reader.uvarint()
        name = data[reader.pos:reader.pos + length].decode("utf-8")
        reader.pos += length
        for _ in range(reader.uvarint()):
            meta = reader.uvarint()
            output[(name, meta)] = BlockState.from_nbt(reader.root())
    return output


def read_palette(data):
    reader = NbtReader(data, network=True)
    while not reader.eof():
        yield BlockState.from_nbt(reader.root())


class Translator:
    """Bedrock state -> Java state through Blocks.json, with the same name-only fallback the app uses"""

    def __init__(self, blocks):
        self.blocks = blocks
        self.names = {}
        for bedrock, java in blocks.items():
            self.names.setdefault(bedrock.split("[")[0], java.split("[")[0])

    def java(self, state, original=None):
        """With original, a state that didn't resolve exactly is only worth listing if the upgrade renamed it;
        otherwise the app's own name fallback gives the same answer"""
        exact = self.blocks.get(state.key())
        if exact is not None or (original is not None and state.name == original.name):
            return exact
        return self.names.get(state.name)


def build(blocks, table_version):
    """blocks is Blocks.json; table_version the Bedrock version it describes, e.g. '1.26.30'"""
    max_version = version_id(*version_of(table_version)[:3], 255)
    upgrader = Upgrader(load_schemas(max_version))
    translator = Translator(blocks)

    # Every state older versions could save, newest palette first so a key that meant different things in
    # different versions resolves the way the most recent version saving it meant it
    states = {}
    palettes = sorted((n for n in list_files(PALETTE_REPO) if n.endswith(".nbt")), key=version_of, reverse=True)
    for name in palettes:
        if version_id(*version_of(name)) > max_version:
            continue
        for state in read_palette(fetch_bytes(RAW.format(repo=PALETTE_REPO, path=name))):
            key = state.key()
            if key in blocks or key in states:
                continue
            java = translator.java(upgrader.upgrade(state), state)
            if java is not None:
                states[key] = java

    # Name + data value palettes (1.2.13 to 1.12) and numeric chunks (before 1.2.13)
    values = {}
    for (name, meta), state in read_id_meta(fetch_bytes(RAW.format(repo=SCHEMA_REPO, path="id_meta_to_nbt/1.12.0.bin"))).items():
        java = translator.java(upgrader.upgrade(state))
        if java is not None:
            values[f"{name}@{meta}"] = java

    legacy_ids = json.loads(fetch_bytes(RAW.format(repo=SCHEMA_REPO, path="block_legacy_id_map.json")))
    ids = {str(i): name for name, i in sorted(legacy_ids.items(), key=lambda x: x[1]) if 0 <= i <= 255}

    return {"States": dict(sorted(states.items())), "Values": dict(sorted(values.items())), "Ids": ids}, len(palettes)

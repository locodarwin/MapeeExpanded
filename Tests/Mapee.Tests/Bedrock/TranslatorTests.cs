using WorldEditor;

namespace Mapee.Tests.Bedrock
{
    public class TranslatorTests
    {
        private static readonly BedrockBlockTranslator Translator = BedrockBlockTranslator.Default;

        [Fact]
        public void UnusedDataBitsFallBackToTheBlocksPlainForm()
        {
            Assert.Equal("minecraft:white_wool", Translator.Translate(BedrockBlockTranslator.Legacy("minecraft:wool", 99)).Name);
            Assert.Equal("minecraft:white_wool", Translator.Translate(BedrockBlockTranslator.Legacy("35", 99)).Name);
        }

        [Fact]
        public void OldStatesTheTablesDontListStillGetAJavaName()
        {
            Block wool = Translator.Translate(new Block("minecraft:wool", [new Property("color", "not_a_colour")]));
            Assert.EndsWith("_wool", wool.Name);
        }

        [Fact]
        public void UnknownBlocksKeepTheirBedrockName()
        {
            Assert.Equal("minecraft:not_a_block", Translator.Translate(new Block("minecraft:not_a_block")).Name);
            Assert.Equal("minecraft:not_a_block", Translator.Translate(BedrockBlockTranslator.Legacy("minecraft:not_a_block", 0)).Name);
        }

        [Fact]
        public void ModernStatesKeepTheirProperties()
        {
            Block stairs = Translator.Translate(new Block("minecraft:oak_stairs", [new Property("upside_down_bit", "1"), new Property("weirdo_direction", "2")]));
            Assert.Equal("minecraft:oak_stairs", stairs.Name);
            Assert.Contains(stairs.Properties, p => p.Name == "half" && p.Value == "top");
        }
    }
}

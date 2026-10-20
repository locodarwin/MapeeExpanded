namespace Mapper
{
    public enum MarkupMode { View, Pin, Zone, Rectangle, Select }
    public enum MarkupButton { Left, Right }
    public enum MarkupKey { Escape, Backspace, Delete, Enter, Undo, Redo }

    // How the map lies on the screen: block coordinates to points on the map surface and back, without rounding
    public interface IMarkupView
    {
        string Dimension { get; }
        (double X, double Y) ToScreen(double x, double z);
        (double X, double Z) ToWorld(double x, double y);
    }

    // What the mouse and keys do to a world's markup, kept apart from either GUI. The GUIs pass in pointer events on the
    // map surface (in its own units) and draw what MarkupFrame says; they ask for a pin or zone's details when
    // EditRequested says so and hand the result to Commit.
    //   Pin: a click drops a pin on the block. Zone: clicks place corners; a double-click, Enter, or a click on the
    //   first corner closes it; Backspace takes the last corner back. Rectangle: a drag. Select: a click picks a pin or
    //   zone, a drag moves a pin or a picked zone's corner, a double-click edits. A right-click edits in any mode.
    //   Corners snap to block corners, or chunk corners with Shift. A click that becomes a drag pans the map instead
    public class MarkupEditor
    {
        public const double PinRadius = 9;
        public const double HandleRadius = 5;
        private const double Slop = 4, DragThreshold = 4, DoubleClickDistance = 6;
        private static readonly TimeSpan DoubleClickTime = TimeSpan.FromMilliseconds(450);
        private const int MaxUndo = 200;

        public IMarkupView View { get; }
        public MarkupDocument Document { get; private set; } = new();

        public MarkupMode Mode
        {
            get => _mode;
            set
            {
                if (_mode == value) return;
                _mode = value;
                CancelGesture();
                if (value == MarkupMode.View) Selected = null;
                ModeChanged?.Invoke(this, EventArgs.Empty);
                Repaint();
            }
        }
        private MarkupMode _mode = MarkupMode.View;

        public bool Visible
        {
            get => _visible;
            set
            {
                if (_visible == value) return;
                _visible = value;
                if (!value) Mode = MarkupMode.View;
                Hovered = null;
                Repaint();
            }
        }
        private bool _visible = true;

        public MarkupItem? Hovered { get; private set; }
        public MarkupItem? Selected { get; private set; }
        public int HoveredCorner { get; private set; } = -1;

        // A new pin or zone waiting for its details; drawn, but not in the document until committed
        public MarkupItem? Pending { get; private set; }

        // The corners of a zone being placed, and the corner the next click would add
        public IReadOnlyList<MarkupPoint> Draft => _draft;
        private readonly List<MarkupPoint> _draft = [];
        public MarkupPoint? DraftCursor { get; private set; }

        // The blocks a rectangle being dragged would hold, both corners included, and whether it grows out to
        // whole chunks (Shift)
        public (MarkupPoint From, MarkupPoint To)? RectangleDrag { get; private set; }
        public bool RectangleToChunks { get; private set; }

        // The corners of the rectangle being dragged
        public List<MarkupPoint>? RectanglePoints()
        {
            if (RectangleDrag is not { } drag) return null;
            var (from, to) = drag;
            if (!RectangleToChunks) return MapZone.Rectangle(from.X, from.Z, to.X, to.Z);

            static int Down(int v) => (int)Math.Floor(v / 16.0) * 16;
            int minX = Down(Math.Min(from.X, to.X)), minZ = Down(Math.Min(from.Z, to.Z));
            int maxX = Down(Math.Max(from.X, to.X)) + 15, maxZ = Down(Math.Max(from.Z, to.Z)) + 15;
            return MapZone.Rectangle(minX, minZ, maxX, maxZ);
        }

        public (double X, double Y)? Mouse { get; private set; }

        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;

        // The look the last new pin or zone was given, for the next one
        public string LastColor { get; set; } = MarkupColors.Default;
        public string? LastIcon { get; set; }

        // Something on the map needs drawing again
        public event EventHandler? Changed;
        // The document changed and should be saved
        public event EventHandler? Edited;
        public event EventHandler? ModeChanged;
        // Show the details of a pin or zone for editing: a copy for one already in the document, the item itself for
        // a new one. Pass it to Commit, Delete or Cancel
        public event EventHandler<MarkupEditRequest>? EditRequested;

        private readonly List<MarkupDocument> _undo = [], _redo = [];

        private enum Drag { None, Pin, Corner, Rectangle }
        private Drag _drag = Drag.None;
        private MapPin? _dragPin;
        private MapZone? _dragZone;
        private int _dragCorner;
        private MarkupDocument? _beforeDrag;

        private bool _down, _moved;
        private MarkupButton _downButton;
        private (double X, double Y) _downAt;
        private DateTime _lastClick = DateTime.MinValue;
        private (double X, double Y) _lastClickAt;

        // Tests set this to step time along
        public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

        public MarkupEditor(IMarkupView view)
        {
            View = view;
        }

        public void Load(MarkupDocument document)
        {
            Document = document;
            _undo.Clear();
            _redo.Clear();
            Pending = null;
            Selected = Hovered = null;
            CancelGesture();
            Repaint();
        }

        // The dimension changed, or the view jumped: drop anything half done
        public void Reset()
        {
            Pending = null;
            Selected = Hovered = null;
            CancelGesture();
            Repaint();
        }

        // Picks a pin or zone from outside the map, as the Places list does
        public void Select(MarkupItem? item)
        {
            Selected = item is null ? null : Find(item.Id);
            Repaint();
        }

        // Returns true when the editor takes the press, and the map shouldn't pan while the button is down
        public bool PointerDown(double x, double y, MarkupButton button, bool shift)
        {
            _down = true;
            _moved = false;
            _downButton = button;
            _downAt = (x, y);
            Mouse = (x, y);

            if (!Visible || button != MarkupButton.Left) return false;

            switch (Mode)
            {
                case MarkupMode.Rectangle:
                    MarkupPoint block = BlockAt(x, y);
                    _drag = Drag.Rectangle;
                    RectangleDrag = (block, block);
                    RectangleToChunks = shift;
                    Repaint();
                    return true;

                case MarkupMode.Select:
                    if (PinAt(x, y) is MapPin pin)
                    {
                        _drag = Drag.Pin;
                        _dragPin = pin;
                        _beforeDrag = Document.Clone();
                        Selected = pin;
                        Repaint();
                        return true;
                    }
                    if (Selected is MapZone zone && CornerAt(zone, x, y) is int corner)
                    {
                        _drag = Drag.Corner;
                        _dragZone = zone;
                        _dragCorner = corner;
                        _beforeDrag = Document.Clone();
                        return true;
                    }
                    return false;
            }
            return false;
        }

        public void PointerMove(double x, double y, bool shift)
        {
            Mouse = (x, y);
            if (_down && Distance((x, y), _downAt) > DragThreshold) _moved = true;
            if (!Visible) return;

            switch (_drag)
            {
                case Drag.Pin when _dragPin is not null:
                    MarkupPoint block = BlockAt(x, y);
                    if (block.X == _dragPin.X && block.Z == _dragPin.Z) return;
                    (_dragPin.X, _dragPin.Z) = (block.X, block.Z);
                    Repaint();
                    return;

                case Drag.Corner when _dragZone is not null:
                    MarkupPoint corner = CornerNear(x, y, shift);
                    if (_dragZone.Points[_dragCorner] == corner) return;
                    _dragZone.Points[_dragCorner] = corner;
                    Repaint();
                    return;

                case Drag.Rectangle when RectangleDrag is { } rectangle:
                    RectangleDrag = (rectangle.From, BlockAt(x, y));
                    RectangleToChunks = shift;
                    Repaint();
                    return;
            }

            bool changed = UpdateHover(x, y);
            if (Mode == MarkupMode.Zone)
            {
                DraftCursor = CornerNear(x, y, shift);
                changed = true;
            }
            if (changed) Repaint();
        }

        public void PointerUp(double x, double y, bool shift)
        {
            if (!_down) return;
            _down = false;
            Mouse = (x, y);
            if (!Visible) return;

            Drag drag = _drag;
            _drag = Drag.None;

            if (_downButton == MarkupButton.Right)
            {
                if (!_moved && ItemAt(x, y) is MarkupItem item)
                {
                    Selected = item;
                    RequestEdit(item, false);
                }
                return;
            }

            switch (drag)
            {
                case Drag.Pin or Drag.Corner:
                    if (_moved && _beforeDrag is not null)
                    {
                        Remember(_beforeDrag);
                        Edited?.Invoke(this, EventArgs.Empty);
                    }
                    _beforeDrag = null;
                    _dragPin = null;
                    _dragZone = null;
                    if (!_moved) Click(x, y, shift);
                    Repaint();
                    return;

                case Drag.Rectangle:
                    RectangleToChunks = shift;
                    List<MarkupPoint>? corners = RectanglePoints();
                    RectangleDrag = null;
                    if (_moved && corners is not null) Propose(new MapZone() { Points = corners });
                    Repaint();
                    return;
            }

            if (!_moved) Click(x, y, shift);
        }

        public void PointerLeft()
        {
            Mouse = null;
            DraftCursor = null;
            if (Hovered is not null || Mode == MarkupMode.Zone)
            {
                Hovered = null;
                Repaint();
            }
        }

        // Returns true when the key did something here
        public bool Key(MarkupKey key)
        {
            if (!Visible) return false;

            switch (key)
            {
                case MarkupKey.Escape:
                    if (_draft.Count > 0 || _drag != Drag.None)
                    {
                        CancelGesture();
                        Repaint();
                        return true;
                    }
                    if (Selected is not null)
                    {
                        Selected = null;
                        Repaint();
                        return true;
                    }
                    return false;

                case MarkupKey.Backspace when Mode == MarkupMode.Zone && _draft.Count > 0:
                    _draft.RemoveAt(_draft.Count - 1);
                    Repaint();
                    return true;

                case MarkupKey.Enter when Mode == MarkupMode.Zone && _draft.Count >= 3:
                    FinishZone();
                    return true;

                case MarkupKey.Delete when Selected is not null && Mode != MarkupMode.View:
                    Delete(Selected);
                    return true;

                case MarkupKey.Undo:
                    return Undo();
                case MarkupKey.Redo:
                    return Redo();
            }
            return false;
        }

        private void Click(double x, double y, bool shift)
        {
            DateTime now = Clock();
            bool isDouble = now - _lastClick <= DoubleClickTime && Distance((x, y), _lastClickAt) <= DoubleClickDistance;
            _lastClick = isDouble ? DateTime.MinValue : now;
            _lastClickAt = (x, y);

            switch (Mode)
            {
                case MarkupMode.Pin:
                    if (PinAt(x, y) is MapPin existing)
                    {
                        Selected = existing;
                        RequestEdit(existing, false);
                        return;
                    }
                    MarkupPoint block = BlockAt(x, y);
                    Propose(new MapPin() { X = block.X, Z = block.Z, Icon = LastIcon });
                    return;

                case MarkupMode.Zone:
                    MarkupPoint corner = CornerNear(x, y, shift);
                    if (_draft.Count >= 3 && (isDouble || IsNearFirstCorner(x, y)))
                    {
                        FinishZone();
                        return;
                    }
                    if (isDouble) return;
                    if (_draft.Count == 0 || _draft[^1] != corner) _draft.Add(corner);
                    Repaint();
                    return;

                case MarkupMode.Select:
                    // On a picked zone, a double-click takes a corner away or adds one on an edge
                    if (isDouble && Selected is MapZone picked && ReshapeAt(picked, x, y, shift))
                    {
                        Repaint();
                        return;
                    }
                    MarkupItem? item = ItemAt(x, y);
                    Selected = item;
                    if (isDouble && item is not null) RequestEdit(item, false);
                    Repaint();
                    return;
            }
        }

        private bool ReshapeAt(MapZone zone, double x, double y, bool shift)
        {
            if (CornerAt(zone, x, y) is int corner)
            {
                if (zone.Points.Count <= 3) return false;
                Remember(Document.Clone());
                zone.Points.RemoveAt(corner);
                HoveredCorner = -1;
                Edited?.Invoke(this, EventArgs.Empty);
                return true;
            }

            for (int i = 0; i < zone.Points.Count; i++)
            {
                MarkupPoint a = zone.Points[i], b = zone.Points[(i + 1) % zone.Points.Count];
                var sa = View.ToScreen(a.X, a.Z);
                var sb = View.ToScreen(b.X, b.Z);
                if (ZoneGeometry.DistanceToSegment(x, y, sa.X, sa.Y, sb.X, sb.Y) > Slop + 2) continue;

                MarkupPoint added = CornerNear(x, y, shift);
                if (added == a || added == b) return false;
                Remember(Document.Clone());
                zone.Points.Insert(i + 1, added);
                Edited?.Invoke(this, EventArgs.Empty);
                return true;
            }
            return false;
        }

        private bool IsNearFirstCorner(double x, double y)
        {
            var first = View.ToScreen(_draft[0].X, _draft[0].Z);
            return Distance((x, y), first) <= HandleRadius + Slop + 2;
        }

        private void FinishZone()
        {
            List<MarkupPoint> points = [.. _draft];
            _draft.Clear();
            DraftCursor = null;

            // A corner placed twice in a row, as the first and last, adds nothing
            if (points.Count > 3 && points[0] == points[^1]) points.RemoveAt(points.Count - 1);
            if (ZoneGeometry.Area(points) == 0)
            {
                Repaint();
                return;
            }
            Propose(new MapZone() { Points = points });
        }

        private void Propose(MarkupItem item)
        {
            item.Dimension = View.Dimension;
            item.Color = LastColor;
            Pending = item;
            Repaint();
            RequestEdit(item, true);
        }

        private void RequestEdit(MarkupItem item, bool isNew)
        {
            EditRequested?.Invoke(this, new MarkupEditRequest(isNew ? item : item.CopyItem(), isNew, item));
        }

        // Saves the details given for a new item, or for a copy of one in the document
        public void Commit(MarkupItem edited)
        {
            if (edited == Pending)
            {
                Remember(Document.Clone());
                if (edited is MapPin pin) Document.Pins.Add(pin);
                else if (edited is MapZone zone) Document.Zones.Add(zone);
                Pending = null;
                // Outside Select mode nothing stays picked once it's placed
                Selected = Mode == MarkupMode.Select ? edited : null;
            }
            else
            {
                if (Find(edited.Id) is not MarkupItem original) return;
                Remember(Document.Clone());
                original.TakeLook(edited);
                Selected = Mode == MarkupMode.Select ? original : null;
            }

            LastColor = edited.Color;
            if (edited is MapPin { Icon: var icon }) LastIcon = icon;

            Edited?.Invoke(this, EventArgs.Empty);
            Repaint();
        }

        // Forgets a new item whose details were never given, or leaves one in the document as it was
        public void Cancel(MarkupItem edited)
        {
            if (edited == Pending) Pending = null;
            else if (Mode != MarkupMode.Select && Selected?.Id == edited.Id) Selected = null;
            Repaint();
        }

        public void Delete(MarkupItem item)
        {
            if (item == Pending)
            {
                Cancel(item);
                return;
            }
            if (Find(item.Id) is not MarkupItem original) return;

            Remember(Document.Clone());
            if (original is MapPin pin) Document.Pins.Remove(pin);
            else if (original is MapZone zone) Document.Zones.Remove(zone);
            if (Selected?.Id == item.Id) Selected = null;
            if (Hovered?.Id == item.Id) Hovered = null;

            Edited?.Invoke(this, EventArgs.Empty);
            Repaint();
        }

        // Brings in markup read from a file; one undo takes it all back out
        public (int Pins, int Zones) Import(MarkupDocument other)
        {
            MarkupDocument before = Document.Clone();
            var added = Document.Merge(other);
            if (added.Pins + added.Zones == 0) return added;

            Remember(before);
            Edited?.Invoke(this, EventArgs.Empty);
            Repaint();
            return added;
        }

        public bool Undo() => Step(_undo, _redo);
        public bool Redo() => Step(_redo, _undo);

        private bool Step(List<MarkupDocument> from, List<MarkupDocument> to)
        {
            if (from.Count == 0) return false;

            CancelGesture();
            Pending = null;
            to.Add(Document.Clone());
            MarkupDocument state = from[^1];
            from.RemoveAt(from.Count - 1);

            Document.Pins = state.Pins;
            Document.Zones = state.Zones;
            Selected = Selected is null ? null : Find(Selected.Id);
            Hovered = null;

            Edited?.Invoke(this, EventArgs.Empty);
            Repaint();
            return true;
        }

        private void Remember(MarkupDocument before)
        {
            _undo.Add(before);
            if (_undo.Count > MaxUndo) _undo.RemoveAt(0);
            _redo.Clear();
        }

        private void CancelGesture()
        {
            _draft.Clear();
            DraftCursor = null;
            RectangleDrag = null;

            // A pin or corner dragged part way goes back where it was
            if (_drag is Drag.Pin or Drag.Corner && _beforeDrag is not null)
            {
                Document.Pins = _beforeDrag.Pins;
                Document.Zones = _beforeDrag.Zones;
                Selected = Selected is null ? null : Find(Selected.Id);
            }
            _drag = Drag.None;
            _beforeDrag = null;
            _dragPin = null;
            _dragZone = null;
        }

        public MarkupItem? Find(string id)
        {
            return (MarkupItem?)Document.Pins.FirstOrDefault(p => p.Id == id) ?? Document.Zones.FirstOrDefault(z => z.Id == id);
        }

        private bool UpdateHover(double x, double y)
        {
            MarkupItem? hovered = ItemAt(x, y);
            int corner = Mode == MarkupMode.Select && Selected is MapZone zone ? CornerAt(zone, x, y) ?? -1 : -1;
            if (hovered == Hovered && corner == HoveredCorner) return false;

            Hovered = hovered;
            HoveredCorner = corner;
            return true;
        }

        // Pins lie over zones, later pins over earlier ones, and smaller zones over the larger ones they sit in
        public MarkupItem? ItemAt(double x, double y) => (MarkupItem?)PinAt(x, y) ?? ZoneAt(x, y);

        public MapPin? PinAt(double x, double y)
        {
            string dimension = View.Dimension;
            for (int i = Document.Pins.Count - 1; i >= 0; i--)
            {
                MapPin pin = Document.Pins[i];
                if (pin.Dimension != dimension) continue;
                if (Distance((x, y), View.ToScreen(pin.X + 0.5, pin.Z + 0.5)) <= PinRadius + Slop) return pin;
            }
            return null;
        }

        public MapZone? ZoneAt(double x, double y)
        {
            var (wx, wz) = View.ToWorld(x, y);
            return Document.ZonesIn(View.Dimension)
                .OrderBy(z => ZoneGeometry.Area(z.Points))
                .FirstOrDefault(z => ZoneGeometry.Contains(z.Points, wx, wz) || IsNearEdge(z, x, y));
        }

        private bool IsNearEdge(MapZone zone, double x, double y)
        {
            for (int i = 0; i < zone.Points.Count; i++)
            {
                var a = View.ToScreen(zone.Points[i].X, zone.Points[i].Z);
                var b = View.ToScreen(zone.Points[(i + 1) % zone.Points.Count].X, zone.Points[(i + 1) % zone.Points.Count].Z);
                if (ZoneGeometry.DistanceToSegment(x, y, a.X, a.Y, b.X, b.Y) <= Slop) return true;
            }
            return false;
        }

        public int? CornerAt(MapZone zone, double x, double y)
        {
            for (int i = zone.Points.Count - 1; i >= 0; i--)
            {
                if (Distance((x, y), View.ToScreen(zone.Points[i].X, zone.Points[i].Z)) <= HandleRadius + Slop) return i;
            }
            return null;
        }

        public MarkupPoint BlockAt(double x, double y)
        {
            var (wx, wz) = View.ToWorld(x, y);
            return new((int)Math.Floor(wx), (int)Math.Floor(wz));
        }

        public MarkupPoint CornerNear(double x, double y, bool toChunk)
        {
            var (wx, wz) = View.ToWorld(x, y);
            int step = toChunk ? 16 : 1;
            return new((int)Math.Round(wx / step) * step, (int)Math.Round(wz / step) * step);
        }

        private static double Distance((double X, double Y) a, (double X, double Y) b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private void Repaint() => Changed?.Invoke(this, EventArgs.Empty);
    }

    // Item is what to show and pass back; Original is the item on the map it describes
    public record MarkupEditRequest(MarkupItem Item, bool IsNew, MarkupItem Original);
}

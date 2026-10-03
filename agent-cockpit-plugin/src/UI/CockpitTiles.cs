namespace Loupedeck.AgentCockpitPlugin
{
    using System;

    /// <summary>
    /// Full-bleed tiles. Agent colors: blue / yellow / green / red (+ dark vacant).
    /// </summary>
    internal static class CockpitTiles
    {
        public static readonly BitmapColor Ink = new BitmapColor(248, 250, 252);
        public static readonly BitmapColor InkDim = new BitmapColor(160, 168, 180);

        public static readonly BitmapColor Empty = new BitmapColor(28, 30, 36);
        public static readonly BitmapColor Running = new BitmapColor(18, 92, 230);
        public static readonly BitmapColor NeedPermission = new BitmapColor(214, 170, 16);
        public static readonly BitmapColor ShellPulseA = new BitmapColor(180, 140, 20);
        public static readonly BitmapColor ShellPulseB = new BitmapColor(230, 190, 40);
        public static readonly BitmapColor Done = new BitmapColor(28, 150, 82);
        public static readonly BitmapColor Error = new BitmapColor(196, 52, 52);
        public static readonly BitmapColor Stopping = new BitmapColor(120, 70, 40);

        public static readonly BitmapColor Approve = new BitmapColor(22, 128, 74);
        public static readonly BitmapColor ApproveHot = new BitmapColor(34, 180, 96);
        public static readonly BitmapColor Deny = new BitmapColor(120, 36, 40);
        public static readonly BitmapColor DenyHot = new BitmapColor(196, 52, 52);
        public static readonly BitmapColor Kill = new BitmapColor(96, 28, 36);
        public static readonly BitmapColor ControlIdle = new BitmapColor(42, 44, 52);
        public static readonly BitmapColor DecisionHot = new BitmapColor(200, 150, 20);

        public static BitmapColor ColorFor(AgentStatus status, Boolean occupied, Int32 pulsePhase = 0)
        {
            if (!occupied)
            {
                return Empty;
            }

            return status switch
            {
                AgentStatus.AwaitingApproval => NeedPermission,
                AgentStatus.ShellRunning => pulsePhase == 0 ? ShellPulseA : ShellPulseB,
                AgentStatus.Stopping => Stopping,
                AgentStatus.Done => Done,
                AgentStatus.Error => Error,
                AgentStatus.Thinking => Running,
                AgentStatus.Idle => Running,
                _ => Empty,
            };
        }

        public static BitmapImage RenderAgentSlot(
            PluginImageSize imageSize,
            AgentSlot slot,
            Boolean isActive,
            Boolean isPinned,
            Int32 pulsePhase = 0)
        {
            using var bb = CreateFullBleed(imageSize, ColorFor(slot.Status, slot.IsOccupied, pulsePhase));
            var w = bb.Width;
            var h = bb.Height;

            if (slot.IsOccupied && (isPinned || isActive))
            {
                var bar = Math.Max(4, h / 14);
                bb.FillRectangle(0, 0, w, bar, isPinned ? Ink : new BitmapColor(170, 195, 255));
            }

            var ink = slot.IsOccupied ? Ink : InkDim;
            var filled = slot.IsOccupied && slot.Status is
                AgentStatus.Thinking
                or AgentStatus.ShellRunning
                or AgentStatus.AwaitingApproval
                or AgentStatus.Stopping
                or AgentStatus.Done
                or AgentStatus.Error
                or AgentStatus.Idle;
            DrawAgentGlyph(bb, slot.Index, ink, filled);

            if (slot.HasPendingDecision && slot.Pending?.Kind == PendingDecisionKind.SwitchMode
                && !String.IsNullOrEmpty(slot.Pending.TargetMode))
            {
                bb.DrawText(
                    $"→ {slot.Pending.TargetMode}",
                    x: 2,
                    y: h - (h / 4),
                    width: w - 4,
                    height: h / 4,
                    color: Ink);
            }

            return bb.ToImage();
        }

        public static BitmapImage RenderControl(
            PluginImageSize imageSize,
            ControlKind kind,
            Int32? agentIndex,
            BitmapColor background,
            Boolean armed,
            String? preview = null)
        {
            using var bb = CreateFullBleed(imageSize, background);
            var w = bb.Width;
            var h = bb.Height;

            var ink = armed ? Ink : InkDim;
            var hasPreview = armed && !String.IsNullOrWhiteSpace(preview)
                && kind is ControlKind.Allow or ControlKind.Run or ControlKind.Switch;

            DrawControlGlyph(bb, kind, ink, background, verticalBias: hasPreview ? -0.12f : 0f);

            if (hasPreview)
            {
                var text = TruncateShell(preview!, maxChars: 18);
                bb.DrawText(text, x: 2, y: (Int32)(h * 0.58), width: w - 4, height: (Int32)(h * 0.38), color: Ink);
            }
            else if (armed && kind is ControlKind.Run or ControlKind.Always or ControlKind.Skip or ControlKind.Switch)
            {
                var label = kind switch
                {
                    ControlKind.Run => "Run",
                    ControlKind.Always => "Always",
                    ControlKind.Skip => "Skip",
                    ControlKind.Switch => "Switch",
                    _ => "",
                };
                if (label.Length > 0)
                {
                    bb.DrawText(label, x: 2, y: (Int32)(h * 0.72), width: w - 4, height: (Int32)(h * 0.24), color: ink);
                }
            }

            if (armed && agentIndex is Int32 idx && idx >= 0 && idx < AgentSlotStore.SlotCount)
            {
                var anchor = kind switch
                {
                    ControlKind.Allow or ControlKind.Run or ControlKind.Switch => GlyphAnchor.BottomLeft,
                    ControlKind.Deny or ControlKind.Always => GlyphAnchor.BottomCenter,
                    ControlKind.Kill or ControlKind.Skip => GlyphAnchor.BottomRight,
                    _ => GlyphAnchor.BottomLeft,
                };
                DrawAgentGlyphSmall(bb, idx, ink, anchor);
            }

            return bb.ToImage();
        }

        private static String TruncateShell(String command, Int32 maxChars)
        {
            var oneLine = command.Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (oneLine.Length <= maxChars)
            {
                return oneLine;
            }

            return oneLine.Substring(0, Math.Max(1, maxChars - 1)) + "…";
        }

        private enum GlyphAnchor
        {
            BottomLeft,
            BottomCenter,
            BottomRight,
        }

        private static BitmapBuilder CreateFullBleed(PluginImageSize imageSize, BitmapColor fill)
        {
            var bb = new BitmapBuilder(imageSize);
            bb.Clear(fill);
            bb.FillRectangle(0, 0, bb.Width, bb.Height, fill);
            return bb;
        }

        public enum ControlKind
        {
            Allow,
            Deny,
            Kill,
            Run,
            Always,
            Skip,
            Switch,
        }

        private static void DrawAgentGlyph(BitmapBuilder bb, Int32 index, BitmapColor ink, Boolean filled)
        {
            var cx = bb.Width / 2;
            var cy = bb.Height / 2;
            var r = Math.Min(bb.Width, bb.Height) * 3 / 8;

            switch (index % 6)
            {
                case 0:
                    if (filled)
                    {
                        bb.FillCircle(cx, cy, r, ink);
                    }
                    else
                    {
                        bb.DrawCircle(cx, cy, r, ink);
                    }

                    break;
                case 1:
                    DrawDiamond(bb, cx, cy, r, ink, filled);
                    break;
                case 2:
                    DrawTriangle(bb, cx, cy, r, ink, filled);
                    break;
                case 3:
                    bb.DrawCircle(cx, cy, r, ink);
                    if (filled)
                    {
                        bb.FillCircle(cx, cy, Math.Max(2, r / 3), ink);
                    }
                    else
                    {
                        bb.DrawCircle(cx, cy, Math.Max(2, r / 2), ink);
                    }

                    break;
                case 4:
                    DrawSpark(bb, cx, cy, r, ink);
                    if (filled)
                    {
                        bb.FillCircle(cx, cy, Math.Max(2, r / 5), ink);
                    }

                    break;
                default:
                    DrawHexHint(bb, cx, cy, r, ink, filled);
                    break;
            }
        }

        private static void DrawAgentGlyphSmall(BitmapBuilder bb, Int32 index, BitmapColor ink, GlyphAnchor anchor)
        {
            var r = Math.Max(5, Math.Min(bb.Width, bb.Height) / 9);
            var pad = Math.Max(4, bb.Width / 16);
            Int32 cx;
            Int32 cy;
            switch (anchor)
            {
                case GlyphAnchor.BottomCenter:
                    cx = bb.Width / 2;
                    cy = bb.Height - r - pad;
                    break;
                case GlyphAnchor.BottomRight:
                    cx = bb.Width - r - pad;
                    cy = bb.Height - r - pad;
                    break;
                default: // BottomLeft
                    cx = r + pad;
                    cy = bb.Height - r - pad;
                    break;
            }

            switch (index % 6)
            {
                case 0:
                    bb.FillCircle(cx, cy, r, ink);
                    break;
                case 1:
                    DrawDiamond(bb, cx, cy, r, ink, filled: true);
                    break;
                case 2:
                    DrawTriangle(bb, cx, cy, r, ink, filled: true);
                    break;
                case 3:
                    bb.DrawCircle(cx, cy, r, ink);
                    bb.FillCircle(cx, cy, Math.Max(1, r / 3), ink);
                    break;
                case 4:
                    DrawSpark(bb, cx, cy, r, ink);
                    break;
                default:
                    DrawHexHint(bb, cx, cy, r, ink, filled: true);
                    break;
            }
        }

        private static void DrawControlGlyph(
            BitmapBuilder bb,
            ControlKind kind,
            BitmapColor ink,
            BitmapColor background,
            Single verticalBias = 0f)
        {
            var cx = bb.Width / 2f;
            var cy = bb.Height * (0.5f + verticalBias);
            var s = Math.Min(bb.Width, bb.Height) * 0.28f;
            var stroke = Math.Max(3f, s / 5f);

            switch (kind)
            {
                case ControlKind.Allow:
                case ControlKind.Run:
                case ControlKind.Switch:
                    Line(bb, cx - s, cy, cx - s / 4f, cy + s, ink, stroke);
                    Line(bb, cx - s / 4f, cy + s, cx + s, cy - s, ink, stroke);
                    break;

                case ControlKind.Deny:
                case ControlKind.Skip:
                    Line(bb, cx - s, cy - s, cx + s, cy + s, ink, stroke);
                    Line(bb, cx + s, cy - s, cx - s, cy + s, ink, stroke);
                    break;

                case ControlKind.Always:
                    // Double-check = always
                    Line(bb, cx - s, cy - s / 6f, cx - s / 4f, cy + s * 0.7f, ink, stroke);
                    Line(bb, cx - s / 4f, cy + s * 0.7f, cx + s, cy - s, ink, stroke);
                    Line(bb, cx - s * 0.7f, cy, cx - s / 5f, cy + s * 0.55f, ink, stroke * 0.85f);
                    break;

                case ControlKind.Kill:
                {
                    bb.FillCircle(cx, cy, s, ink);
                    var barW = (Int32)(s + s / 2f);
                    var barH = Math.Max(3, (Int32)(s / 3f));
                    bb.FillRectangle((Int32)(cx - barW / 2f), (Int32)(cy - barH / 2f), barW, barH, background);
                    break;
                }
            }
        }

        private static void DrawDiamond(BitmapBuilder bb, Int32 cx, Int32 cy, Int32 r, BitmapColor ink, Boolean filled)
        {
            var stroke = Math.Max(2f, r / 6f);
            Line(bb, cx, cy - r, cx + r, cy, ink, stroke);
            Line(bb, cx + r, cy, cx, cy + r, ink, stroke);
            Line(bb, cx, cy + r, cx - r, cy, ink, stroke);
            Line(bb, cx - r, cy, cx, cy - r, ink, stroke);
            if (filled)
            {
                bb.FillCircle(cx, cy, Math.Max(2, r / 3), ink);
            }
        }

        private static void DrawTriangle(BitmapBuilder bb, Int32 cx, Int32 cy, Int32 r, BitmapColor ink, Boolean filled)
        {
            var stroke = Math.Max(2f, r / 6f);
            Line(bb, cx, cy - r, cx + r, cy + r, ink, stroke);
            Line(bb, cx + r, cy + r, cx - r, cy + r, ink, stroke);
            Line(bb, cx - r, cy + r, cx, cy - r, ink, stroke);
            if (filled)
            {
                bb.FillCircle(cx, cy + r / 6, Math.Max(2, r / 3), ink);
            }
        }

        private static void DrawSpark(BitmapBuilder bb, Int32 cx, Int32 cy, Int32 r, BitmapColor ink)
        {
            var stroke = Math.Max(2f, r / 6f);
            Line(bb, cx - r, cy, cx + r, cy, ink, stroke);
            Line(bb, cx, cy - r, cx, cy + r, ink, stroke);
            var d = r * 0.7f;
            Line(bb, cx - d, cy - d, cx + d, cy + d, ink, stroke);
            Line(bb, cx + d, cy - d, cx - d, cy + d, ink, stroke);
        }

        private static void DrawHexHint(BitmapBuilder bb, Int32 cx, Int32 cy, Int32 r, BitmapColor ink, Boolean filled)
        {
            var stroke = Math.Max(2f, r / 6f);
            var dx = r;
            var dy = (Int32)(r * 0.6);
            var points = new (Int32 x, Int32 y)[]
            {
                (cx - dx / 2, cy - dy),
                (cx + dx / 2, cy - dy),
                (cx + dx, cy),
                (cx + dx / 2, cy + dy),
                (cx - dx / 2, cy + dy),
                (cx - dx, cy),
            };

            for (var i = 0; i < points.Length; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % points.Length];
                Line(bb, a.x, a.y, b.x, b.y, ink, stroke);
            }

            if (filled)
            {
                bb.FillCircle(cx, cy, Math.Max(2, r / 3), ink);
            }
        }

        private static void Line(
            BitmapBuilder bb,
            Single x1,
            Single y1,
            Single x2,
            Single y2,
            BitmapColor color,
            Single strokeWidth) =>
            bb.DrawLine(x1, y1, x2, y2, color, strokeWidth);

        public static readonly BitmapColor Deck = new BitmapColor(36, 40, 50);
        public static readonly BitmapColor DeckLive = new BitmapColor(22, 70, 150);

        public enum DeckIcon
        {
            Tests,
            Explain,
            Types,
            Refactor,
            Review,
            Fix,
            Agent,
            Chat,
            Edit,
            Palette,
            QuickOpen,
            Sidebar,
            Terminal,
            Accept,
            Reject,
            AtFile,
            AtSel,
            AtDiff,
            Usage,
            Mode,
            Model,
            HunkPrev,
            HunkNext,
            Undo,
            Redo,
            Problems,
            ArrowUp,
            ArrowDown,
            Effort,
            Exit,
        }

        /// <summary>Label tile for pages 2–4. Does not affect page-1 agent/control glyphs.</summary>
        public static BitmapImage RenderLabelTile(
            PluginImageSize imageSize,
            String title,
            String? subtitle = null,
            BitmapColor? background = null,
            Boolean dim = false)
        {
            using var bb = CreateFullBleed(imageSize, background ?? Deck);
            var w = bb.Width;
            var h = bb.Height;
            var ink = dim ? InkDim : Ink;
            var hasSub = !String.IsNullOrWhiteSpace(subtitle);

            bb.DrawText(
                title,
                x: 3,
                y: hasSub ? (Int32)(h * 0.16) : (Int32)(h * 0.32),
                width: w - 6,
                height: hasSub ? (Int32)(h * 0.38) : (Int32)(h * 0.40),
                color: ink);

            if (hasSub)
            {
                bb.DrawText(
                    subtitle!,
                    x: 3,
                    y: (Int32)(h * 0.54),
                    width: w - 6,
                    height: (Int32)(h * 0.36),
                    color: dim ? InkDim : Ink);
            }

            return bb.ToImage();
        }

        public static BitmapImage RenderUsageTile(
            PluginImageSize imageSize,
            String title,
            String value)
        {
            using var bb = CreateFullBleed(imageSize, DeckLive);
            var w = bb.Width;
            var h = bb.Height;
            bb.DrawText(
                title,
                x: 3,
                y: (Int32)(h * 0.10),
                width: w - 6,
                height: (Int32)(h * 0.32),
                color: Ink);
            bb.DrawText(
                String.IsNullOrWhiteSpace(value) ? "0" : value,
                x: 3,
                y: (Int32)(h * 0.44),
                width: w - 6,
                height: (Int32)(h * 0.46),
                color: Ink);
            return bb.ToImage();
        }

        public static BitmapImage RenderDeckTile(
            PluginImageSize imageSize,
            DeckIcon icon,
            String label,
            String? subtitle = null,
            BitmapColor? background = null,
            Boolean dim = false)
        {
            using var bb = CreateFullBleed(imageSize, background ?? Deck);
            DrawDeckIcon(bb, icon, dim ? InkDim : Ink, background ?? Deck);
            return bb.ToImage();
        }

        private static void DrawDeckIcon(BitmapBuilder bb, DeckIcon icon, BitmapColor ink, BitmapColor background)
        {
            var cx = bb.Width / 2f;
            var cy = bb.Height / 2f;
            var s = Math.Min(bb.Width, bb.Height) * 3f / 8f;
            var stroke = Math.Max(3f, s / 6f);

            switch (icon)
            {
                case DeckIcon.Tests:
                    Line(bb, cx - s * 0.4f, cy - s, cx + s * 0.4f, cy - s, ink, stroke);
                    Line(bb, cx - s * 0.4f, cy - s, cx - s, cy + s, ink, stroke);
                    Line(bb, cx + s * 0.4f, cy - s, cx + s, cy + s, ink, stroke);
                    Line(bb, cx - s, cy + s, cx + s, cy + s, ink, stroke);
                    bb.FillCircle((Int32)cx, (Int32)(cy + s * 0.15f), Math.Max(2, (Int32)(s / 4)), ink);
                    break;
                case DeckIcon.Explain:
                    bb.DrawCircle((Int32)cx, (Int32)cy, (Int32)s, ink);
                    bb.DrawText("?", x: (Int32)(cx - s), y: (Int32)(cy - s * 0.7f), width: (Int32)(s * 2), height: (Int32)(s * 1.6f), color: ink);
                    break;
                case DeckIcon.Types:
                    Line(bb, cx - s, cy - s, cx + s, cy - s, ink, stroke);
                    Line(bb, cx, cy - s, cx, cy + s, ink, stroke);
                    break;
                case DeckIcon.Refactor:
                    Line(bb, cx - s, cy - s * 0.2f, cx + s * 0.2f, cy - s, ink, stroke);
                    Line(bb, cx + s * 0.2f, cy - s, cx + s, cy - s * 0.2f, ink, stroke);
                    Line(bb, cx + s, cy + s * 0.2f, cx - s * 0.2f, cy + s, ink, stroke);
                    Line(bb, cx - s * 0.2f, cy + s, cx - s, cy + s * 0.2f, ink, stroke);
                    break;
                case DeckIcon.Review:
                    bb.DrawCircle((Int32)cx, (Int32)cy, (Int32)s, ink);
                    bb.FillCircle((Int32)cx, (Int32)cy, Math.Max(2, (Int32)(s / 3)), ink);
                    break;
                case DeckIcon.Fix:
                    Line(bb, cx - s, cy + s * 0.4f, cx + s * 0.2f, cy - s, ink, stroke);
                    Line(bb, cx + s * 0.2f, cy - s, cx + s, cy - s * 0.4f, ink, stroke);
                    Line(bb, cx - s * 0.2f, cy + s, cx + s * 0.5f, cy + s * 0.2f, ink, stroke);
                    break;
                case DeckIcon.Agent:
                    bb.FillCircle((Int32)cx, (Int32)cy, (Int32)s, ink);
                    break;
                case DeckIcon.Chat:
                    bb.FillRectangle((Int32)(cx - s), (Int32)(cy - s * 0.7f), (Int32)(s * 2), (Int32)(s * 1.3f), ink);
                    Line(bb, cx - s * 0.3f, cy + s * 0.6f, cx - s * 0.7f, cy + s, background, stroke);
                    break;
                case DeckIcon.Edit:
                    Line(bb, cx - s, cy + s, cx + s * 0.3f, cy - s * 0.6f, ink, stroke);
                    Line(bb, cx + s * 0.3f, cy - s * 0.6f, cx + s, cy - s, ink, stroke);
                    Line(bb, cx - s, cy + s, cx - s * 0.4f, cy + s, ink, stroke);
                    break;
                case DeckIcon.Palette:
                    bb.DrawRectangle((Int32)(cx - s), (Int32)(cy - s), (Int32)(s * 2), (Int32)(s * 2), ink);
                    Line(bb, cx - s * 0.4f, cy, cx + s * 0.4f, cy, ink, stroke);
                    break;
                case DeckIcon.QuickOpen:
                    Line(bb, cx - s, cy - s * 0.2f, cx - s * 0.2f, cy - s, ink, stroke);
                    Line(bb, cx - s * 0.2f, cy - s, cx + s, cy - s, ink, stroke);
                    Line(bb, cx + s, cy - s, cx + s, cy + s, ink, stroke);
                    Line(bb, cx + s, cy + s, cx - s, cy + s, ink, stroke);
                    Line(bb, cx - s, cy + s, cx - s, cy - s * 0.2f, ink, stroke);
                    break;
                case DeckIcon.Sidebar:
                    Line(bb, cx - s, cy - s, cx + s, cy - s, ink, stroke);
                    Line(bb, cx + s, cy - s, cx + s, cy + s, ink, stroke);
                    Line(bb, cx + s, cy + s, cx - s, cy + s, ink, stroke);
                    Line(bb, cx - s, cy + s, cx - s, cy - s, ink, stroke);
                    Line(bb, cx - s * 0.2f, cy - s, cx - s * 0.2f, cy + s, ink, stroke);
                    break;
                case DeckIcon.Terminal:
                    Line(bb, cx - s, cy - s * 0.3f, cx - s * 0.2f, cy, ink, stroke);
                    Line(bb, cx - s * 0.2f, cy, cx - s, cy + s * 0.3f, ink, stroke);
                    Line(bb, cx, cy + s * 0.5f, cx + s, cy + s * 0.5f, ink, stroke);
                    break;
                case DeckIcon.Accept:
                    Line(bb, cx - s, cy, cx - s / 4f, cy + s, ink, stroke);
                    Line(bb, cx - s / 4f, cy + s, cx + s, cy - s, ink, stroke);
                    break;
                case DeckIcon.Reject:
                    Line(bb, cx - s, cy - s, cx + s, cy + s, ink, stroke);
                    Line(bb, cx + s, cy - s, cx - s, cy + s, ink, stroke);
                    break;
                case DeckIcon.AtFile:
                    bb.DrawText("@", x: (Int32)(cx - s), y: (Int32)(cy - s), width: (Int32)(s * 2), height: (Int32)(s * 2), color: ink);
                    break;
                case DeckIcon.AtSel:
                    bb.DrawText("@s", x: (Int32)(cx - s * 1.2f), y: (Int32)(cy - s), width: (Int32)(s * 2.4f), height: (Int32)(s * 2), color: ink);
                    break;
                case DeckIcon.AtDiff:
                    Line(bb, cx - s, cy, cx + s, cy, ink, stroke);
                    Line(bb, cx, cy - s, cx, cy + s, ink, stroke);
                    break;
                case DeckIcon.Usage:
                    bb.FillRectangle((Int32)(cx - s), (Int32)(cy + s * 0.2f), Math.Max(3, (Int32)(s * 0.4f)), (Int32)(s * 0.8f), ink);
                    bb.FillRectangle((Int32)(cx - s * 0.2f), (Int32)(cy - s * 0.3f), Math.Max(3, (Int32)(s * 0.4f)), (Int32)(s * 1.3f), ink);
                    bb.FillRectangle((Int32)(cx + s * 0.4f), (Int32)(cy - s), Math.Max(3, (Int32)(s * 0.4f)), (Int32)(s * 2f), ink);
                    break;
                case DeckIcon.Mode:
                    Line(bb, cx - s, cy - s * 0.4f, cx + s, cy - s * 0.4f, ink, stroke);
                    Line(bb, cx + s * 0.4f, cy - s, cx + s, cy - s * 0.4f, ink, stroke);
                    Line(bb, cx + s, cy + s * 0.4f, cx - s, cy + s * 0.4f, ink, stroke);
                    Line(bb, cx - s * 0.4f, cy + s, cx - s, cy + s * 0.4f, ink, stroke);
                    break;
                case DeckIcon.Model:
                    bb.DrawRectangle((Int32)(cx - s), (Int32)(cy - s * 0.6f), (Int32)(s * 2), (Int32)(s * 1.2f), ink);
                    bb.FillCircle((Int32)(cx - s * 0.4f), (Int32)cy, Math.Max(2, (Int32)(s / 5)), ink);
                    bb.FillCircle((Int32)(cx + s * 0.4f), (Int32)cy, Math.Max(2, (Int32)(s / 5)), ink);
                    break;
                case DeckIcon.HunkPrev:
                    Line(bb, cx + s * 0.4f, cy - s, cx - s, cy, ink, stroke);
                    Line(bb, cx - s, cy, cx + s * 0.4f, cy + s, ink, stroke);
                    break;
                case DeckIcon.HunkNext:
                    Line(bb, cx - s * 0.4f, cy - s, cx + s, cy, ink, stroke);
                    Line(bb, cx + s, cy, cx - s * 0.4f, cy + s, ink, stroke);
                    break;
                case DeckIcon.Undo:
                    Line(bb, cx + s * 0.6f, cy - s * 0.6f, cx - s, cy - s * 0.6f, ink, stroke);
                    Line(bb, cx - s, cy - s * 0.6f, cx - s * 0.3f, cy + s * 0.2f, ink, stroke);
                    break;
                case DeckIcon.Redo:
                    Line(bb, cx - s * 0.6f, cy - s * 0.6f, cx + s, cy - s * 0.6f, ink, stroke);
                    Line(bb, cx + s, cy - s * 0.6f, cx + s * 0.3f, cy + s * 0.2f, ink, stroke);
                    break;
                case DeckIcon.Problems:
                    Line(bb, cx, cy - s, cx + s, cy + s, ink, stroke);
                    Line(bb, cx + s, cy + s, cx - s, cy + s, ink, stroke);
                    Line(bb, cx - s, cy + s, cx, cy - s, ink, stroke);
                    bb.FillCircle((Int32)cx, (Int32)(cy + s * 0.15f), Math.Max(2, (Int32)(s / 6)), ink);
                    break;
                case DeckIcon.ArrowUp:
                    Line(bb, cx, cy - s, cx - s, cy + s * 0.3f, ink, stroke);
                    Line(bb, cx, cy - s, cx + s, cy + s * 0.3f, ink, stroke);
                    break;
                case DeckIcon.ArrowDown:
                    Line(bb, cx, cy + s, cx - s, cy - s * 0.3f, ink, stroke);
                    Line(bb, cx, cy + s, cx + s, cy - s * 0.3f, ink, stroke);
                    break;
                case DeckIcon.Effort:
                    bb.FillRectangle((Int32)(cx - s), (Int32)(cy + s * 0.35f), Math.Max(3, (Int32)(s * 0.35f)), (Int32)(s * 0.65f), ink);
                    bb.FillRectangle((Int32)(cx - s * 0.18f), (Int32)(cy - s * 0.15f), Math.Max(3, (Int32)(s * 0.35f)), (Int32)(s * 1.15f), ink);
                    bb.FillRectangle((Int32)(cx + s * 0.45f), (Int32)(cy - s), Math.Max(3, (Int32)(s * 0.35f)), (Int32)(s * 2f), ink);
                    break;
                case DeckIcon.Exit:
                    Line(bb, cx - s, cy - s, cx + s, cy + s, ink, stroke);
                    Line(bb, cx + s, cy - s, cx - s, cy + s, ink, stroke);
                    break;
            }
        }
    }
}

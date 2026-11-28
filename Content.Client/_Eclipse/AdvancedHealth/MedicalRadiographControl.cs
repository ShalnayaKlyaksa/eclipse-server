using System.Numerics;
using Content.Shared._Eclipse.AdvancedHealth;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;

namespace Content.Client._Eclipse.AdvancedHealth;

/// <summary>Stylised contour radiography in the same visual language as the medical minigames.</summary>
internal sealed class MedicalRadiographControl(MedicalDiagnosticReport report) : Control
{
    private static readonly Color Bone = Color.FromHex("#d4d9ce");
    private static readonly Color Dim = Color.FromHex("#5a6965");
    private static readonly Color Film = Color.FromHex("#090e10");
    private static readonly Vector2[] Shaft =
    {
        new(-7,-98), new(-19,-94), new(-23,-84), new(-18,-73), new(-9,-64),
        new(-8,-35), new(-6, 0), new(-8, 39), new(-10, 65), new(-20, 78),
        new(-21, 91), new(-11, 99), new(1, 96), new(11, 100), new(21, 92),
        new(22, 80), new(12, 66), new(8, 40), new(6, 0), new(7,-35), new(9,-65),
        new(19,-76), new(20,-88), new(10,-98), new(0,-94),
    };

    protected override void Draw(DrawingHandleScreen h)
    {
        base.Draw(h);
        h.DrawRect(PixelSizeBox, Film);
        var previous = h.GetTransform();
        var scale = Math.Min(Size.X / 320, Size.Y / 260);
        var origin = (Size - new Vector2(320, 260) * scale) / 2;
        h.SetTransform(Matrix3x2.CreateScale(scale * UIScale) * Matrix3x2.CreateTranslation(origin * UIScale) * previous);
        try
        {
            for (var y = 12; y < 250; y += 8)
                h.DrawLine(new Vector2(12, y), new Vector2(308, y), Dim.WithAlpha(0.07f));
            // Film corners and a scale provide framing without covering the anatomy.
            foreach (var (x, y, dx, dy) in new[] { (12f, 12f, 1f, 1f), (308f, 12f,-1f, 1f), (12f, 248f, 1f,-1f), (308f, 248f,-1f,-1f) })
            {
                h.DrawLine(new Vector2(x, y), new Vector2(x+dx*16, y), Dim);
                h.DrawLine(new Vector2(x, y), new Vector2(x, y+dy*16), Dim);
            }
            for (var y = 40; y <= 220; y += 10)
                h.DrawLine(new Vector2(285, y), new Vector2(y % 50 == 0 ? 276 : 281, y), Dim);
            switch (report.Slot)
            {
                case BodyPartSlot.LeftForearm:
                case BodyPartSlot.RightForearm:
                case BodyPartSlot.LeftShin:
                case BodyPartSlot.RightShin:
                    DrawBone(h, new Vector2(135, 130),new Vector2(0.6f, 1),true);
                    DrawBone(h, new Vector2(179, 130),new Vector2(0.42f, 0.96f),false);
                    break;
                case BodyPartSlot.LeftHand:
                case BodyPartSlot.RightHand:
                case BodyPartSlot.LeftFoot:
                case BodyPartSlot.RightFoot:
                    DrawDigits(h);
                    break;
                case BodyPartSlot.Head:
                    DrawSkull(h);
                    break;
                case BodyPartSlot.Chest:
                    DrawRibs(h);
                    break;
                case BodyPartSlot.Abdomen:
                case BodyPartSlot.Neck:
                    DrawSpine(h, report.Slot == BodyPartSlot.Neck ? 7 : 5);
                    break;
                case BodyPartSlot.Pelvis:
                    DrawPelvis(h);
                    break;
                default:
                    DrawBone(h, new Vector2(155, 130),Vector2.One, true);
                    break;
            }
            if (report.Findings.Contains("diagnostic-foreign-body"))
            {
                var p = new Vector2(213, 145);
                Outline(h, p, new[] { new Vector2(-5,-10),new Vector2(8,-4),new Vector2(4, 11),new Vector2(-7, 6) },Bone);
                h.DrawLine(p+new Vector2(13,-4),p+new Vector2(36,-27),Dim);
            }
        }
        finally { h.SetTransform(previous); }
    }

    private void DrawBone(DrawingHandleScreen h, Vector2 center, Vector2 size, bool affected)
    {
        var broken = affected && report.Findings.Contains("diagnostic-fracture");
        Vector2 Point(Vector2 p) => center + p * size + (broken && p.Y >= 0 ? new Vector2(9, 7) : Vector2.Zero);
        for (var i=0;i<Shaft.Length;i++)
        {
            var a=Shaft[i]; var b=Shaft[(i+1)%Shaft.Length];
            if (broken && (a.Y < 0) != (b.Y < 0)) continue;
            h.DrawLine(Point(a),Point(b),Bone);
            h.DrawLine(Point(a*new Vector2(0.78f, 0.98f)),Point(b*new Vector2(0.78f, 0.98f)),Dim);
        }
        h.DrawLine(Point(new Vector2(-3,-62)),Point(new Vector2(-2,-12)),Dim);
        h.DrawLine(Point(new Vector2(3, 12)),Point(new Vector2(4, 63)),Dim);
        if (broken)
        {
            Outline(h, center, new[] { new Vector2(-7,-4),new Vector2(-1, 1),new Vector2(2,-7),new Vector2(7,-2) },Bone, false);
            Outline(h, center+new Vector2(9, 7),new[] { new Vector2(-7, 2),new Vector2(-1, 7),new Vector2(2,-1),new Vector2(7, 4) },Bone, false);
        }
        if (affected && report.Findings.Contains("diagnostic-fixation"))
        {
            h.DrawLine(center+new Vector2(-19,-42),center+new Vector2(-19, 42),Bone);
            for (var y=-35;y<=35;y+=14) h.DrawLine(center+new Vector2(-23, y),center+new Vector2(9, y-3),Bone);
        }
    }

    private void DrawDigits(DrawingHandleScreen h)
    {
        for(var finger=0;finger<5;finger++)
        {
            var x=99+finger*25; var top=50+Math.Abs(finger-2)*15;
            for(var joint=0;joint<3;joint++)
                Outline(h, new Vector2(x, top+joint*24),new[] { new Vector2(-5,-8),new Vector2(4,-8),new Vector2(6, 8),new Vector2(-4, 10) },Bone);
            DrawBone(h, new Vector2(x+5, 178),new Vector2(0.22f, 0.27f),finger==2);
        }
        for(var i=0;i<4;i++) Outline(h, new Vector2(121+i*20, 223),new[] { new Vector2(-7,-7),new Vector2(7,-5),new Vector2(8, 6),new Vector2(-6, 8) },Dim);
    }

    private void DrawRibs(DrawingHandleScreen h)
    {
        DrawSpine(h, 9);
        for(var rib=0;rib<8;rib++)
            for(var side=-1;side<=1;side+=2)
                Arc(h, new Vector2(158, 69+rib*17),new Vector2(side*(70+MathF.Sin(rib*0.35f)*23),20),-1.4f, 1.5f, Bone);
    }

    private static void DrawSpine(DrawingHandleScreen h, int count)
    {
        for(var i=0;i<count;i++)
            Outline(h, new Vector2(158, 35+i*(190f/count)),new[] { new Vector2(-15,-6),new Vector2(15,-6),new Vector2(19, 4),new Vector2(11, 9),new Vector2(-11, 9),new Vector2(-19, 4) },Bone);
    }

    private static void DrawSkull(DrawingHandleScreen h)
    {
        Arc(h, new Vector2(158, 118),new Vector2(66, 88),-MathF.PI, MathF.PI, Bone);
        Arc(h, new Vector2(132, 121),new Vector2(18, 22),-MathF.PI, MathF.PI, Dim);
        Arc(h, new Vector2(184, 121),new Vector2(18, 22),-MathF.PI, MathF.PI, Dim);
        Outline(h, new Vector2(158, 175),new[] { new Vector2(-37,-9),new Vector2(-28, 28),new Vector2(0, 41),new Vector2(28, 28),new Vector2(37,-9) },Bone, false);
        for(var i=-3;i<=3;i++) h.DrawLine(new Vector2(158+i*7, 173),new Vector2(158+i*7, 183),Dim);
    }

    private static void DrawPelvis(DrawingHandleScreen h)
    {
        for(var side=-1;side<=1;side+=2)
        {
            Arc(h, new Vector2(158+side*43, 122),new Vector2(49, 72),-MathF.PI, MathF.PI, Bone);
            Arc(h, new Vector2(158+side*35, 161),new Vector2(20, 27),-MathF.PI, MathF.PI, Dim);
        }
        Outline(h, new Vector2(158, 103),new[] { new Vector2(-19,-41),new Vector2(19,-41),new Vector2(13, 20),new Vector2(0, 39),new Vector2(-13, 20) },Bone);
    }

    private static void Outline(DrawingHandleScreen h, Vector2 origin, Vector2[] points, Color color, bool closed=true)
    {
        for(var i=0;i<points.Length-(closed?0:1);i++) h.DrawLine(origin+points[i],origin+points[(i+1)%points.Length],color);
    }

    private static void Arc(DrawingHandleScreen h, Vector2 center, Vector2 radius, float start, float end, Color color)
    {
        for(var i=0;i<40;i++)
        {
            var a=start+(end-start)*i/40; var b=start+(end-start)*(i+1)/40;
            h.DrawLine(center+new Vector2(MathF.Cos(a),MathF.Sin(a))*radius, center+new Vector2(MathF.Cos(b),MathF.Sin(b))*radius, color);
        }
    }
}

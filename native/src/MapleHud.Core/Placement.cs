using System;

namespace MapleHud.Core
{
    /// <summary>
    /// HUD 위치 계산. 설정은 "모니터의 어느 모서리에서 얼마나 떨어져 있는지"(AlignX/Y + OffsetX/Y, DIP 단위)로 저장한다.
    /// 좌표 대신 모서리 기준으로 두면 카드를 접고 펼쳐 패널 높이가 바뀌어도 화면 안쪽으로 자라고,
    /// 해상도·배율이 바뀌어도 같은 자리에 붙어 있다. 끌어서 옮기면 가까운 모서리를 기준으로 다시 잡는다.
    /// 좌표는 모두 화면 픽셀, dpiScale은 그 모니터의 배율(1.0 = 100%)이다.
    /// </summary>
    public static class Placement
    {
        private const float EdgeMargin = 24;   // 패널이 화면 끝까지 닿지 않게 남기는 여백 (DIP)

        /// <summary>패널 왼쪽 위 좌표</summary>
        public static (int X, int Y) Locate(HudSettings s, int waX, int waY, int waW, int waH, int w, int h, float dpiScale)
        {
            int offX = (int)Math.Round(s.OffsetX * dpiScale), offY = (int)Math.Round(s.OffsetY * dpiScale);
            int x = s.AlignX == "left" ? waX + offX : s.AlignX == "center" ? waX + (waW - w) / 2 : waX + waW - offX - w;
            int y = s.AlignY == "top" ? waY + offY : s.AlignY == "center" ? waY + (waH - h) / 2 : waY + waH - offY - h;
            return (x, y);
        }

        /// <summary>패널이 쓸 수 있는 최대 높이 (화면 픽셀). 기준 모서리 반대쪽에도 여백을 남긴다</summary>
        public static int MaxHeight(HudSettings s, int waH, float dpiScale)
        {
            int offY = (int)Math.Round(s.OffsetY * dpiScale);
            if (s.AlignY == "center") return waH - 2 * offY;
            return waH - offY - Math.Min(offY, (int)Math.Round(EdgeMargin * dpiScale));
        }

        /// <summary>
        /// 끌어서 놓은 자리(x, y, w, h)를 설정으로 바꾼다. dx, dy는 끈 거리.
        /// - 거의 안 움직인 방향은 원래 기준 모서리를 그대로 둔다 (옆으로만 옮겼는데 위아래 기준이 바뀌지 않게)
        /// - 움직인 방향은 패널 가운데가 화면의 어느 쪽 반에 있는지로 기준을 정한다
        /// - 화면 높이의 절반보다 큰 패널은 위 기준으로 둔다 (놓은 자리 아래로 남은 만큼만 쓰고 넘치면 스크롤)
        /// 화면 밖으로 나간 만큼은 안으로 들인다.
        /// </summary>
        public static void FromBox(HudSettings s, int x, int y, int w, int h, int dx, int dy,
            int waX, int waY, int waW, int waH, float dpiScale)
        {
            const int Still = 10;
            int minVisible = (int)Math.Round(160 * dpiScale);

            x = Math.Max(waX, Math.Min(x, waX + waW - w));
            if (Math.Abs(dx) >= Still) s.AlignX = x + w / 2.0 < waX + waW / 2.0 ? "left" : "right";
            if (s.AlignX == "left") s.OffsetX = Dip(x - waX, dpiScale);
            else if (s.AlignX == "right") s.OffsetX = Dip(waX + waW - (x + w), dpiScale);

            if (Math.Abs(dy) >= Still) s.AlignY = h > waH / 2 || y + h / 2.0 < waY + waH / 2.0 ? "top" : "bottom";
            if (s.AlignY == "top")
            {
                int top = Math.Max(waY, Math.Min(y, waY + waH - minVisible));
                s.OffsetY = Dip(top - waY, dpiScale);
            }
            else if (s.AlignY == "bottom")
            {
                int bottom = Math.Min(waY + waH, Math.Max(y + h, waY + minVisible));
                s.OffsetY = Dip(waY + waH - bottom, dpiScale);
            }
        }

        private static int Dip(int px, float dpiScale) => Math.Max(0, (int)Math.Round(px / dpiScale));
    }
}

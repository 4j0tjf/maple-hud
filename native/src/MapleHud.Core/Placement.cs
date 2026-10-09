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
        /// 끌어서 놓은 자리(x, y, w, h)를 설정으로 바꾼다. 패널 가운데가 화면의 어느 쪽 반에 있는지로
        /// 기준 모서리를 정하고(왼쪽/오른쪽, 위/아래), 화면 밖으로 나간 만큼은 안으로 들인다.
        /// </summary>
        public static void FromBox(HudSettings s, int x, int y, int w, int h, int waX, int waY, int waW, int waH, float dpiScale)
        {
            x = Math.Max(waX, Math.Min(x, waX + waW - w));
            y = Math.Max(waY, Math.Min(y, waY + waH - h));
            bool left = x + w / 2.0 < waX + waW / 2.0;
            bool top = y + h / 2.0 < waY + waH / 2.0;
            s.AlignX = left ? "left" : "right";
            s.AlignY = top ? "top" : "bottom";
            s.OffsetX = Math.Max(0, (int)Math.Round((left ? x - waX : waX + waW - (x + w)) / dpiScale));
            s.OffsetY = Math.Max(0, (int)Math.Round((top ? y - waY : waY + waH - (y + h)) / dpiScale));
        }
    }
}

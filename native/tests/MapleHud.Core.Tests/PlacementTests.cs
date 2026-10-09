using Xunit;

namespace MapleHud.Core.Tests
{
    public class PlacementTests
    {
        // 150% 배율 모니터(2560x1400 작업 영역)가 주 모니터 오른쪽에 있는 경우
        private const int WaX = 1920, WaY = 0, WaW = 2560, WaH = 1400;
        private const float Dpi = 1.5f;

        [Fact]
        public void 기본값은_오른쪽_위에서_48만큼()
        {
            var s = new HudSettings();
            var (x, y) = Placement.Locate(s, WaX, WaY, WaW, WaH, 600, 900, Dpi);
            Assert.Equal(WaX + WaW - 72 - 600, x);
            Assert.Equal(WaY + 72, y);
        }

        [Theory]
        [InlineData(2000, 100, "left", "top")]
        [InlineData(3800, 100, "right", "top")]
        [InlineData(2000, 1000, "left", "bottom")]
        [InlineData(3500, 900, "right", "bottom")]
        public void 끌어서_놓은_자리는_가까운_모서리_기준으로_저장하고_그대로_다시_놓인다(int x, int y, string ax, string ay)
        {
            var s = new HudSettings();
            Placement.FromBox(s, x, y, 600, 300, 500, 500, WaX, WaY, WaW, WaH, Dpi);
            Assert.Equal(ax, s.AlignX);
            Assert.Equal(ay, s.AlignY);
            var (x2, y2) = Placement.Locate(s, WaX, WaY, WaW, WaH, 600, 300, Dpi);
            Assert.InRange(x2 - x, -1, 1);
            Assert.InRange(y2 - y, -1, 1);
        }

        [Fact]
        public void 아래_기준이면_패널이_커질_때_위로_자란다()
        {
            var s = new HudSettings();
            Placement.FromBox(s, 2000, 1000, 600, 300, 0, 500, WaX, WaY, WaW, WaH, Dpi);
            var (_, small) = Placement.Locate(s, WaX, WaY, WaW, WaH, 600, 300, Dpi);
            var (_, tall) = Placement.Locate(s, WaX, WaY, WaW, WaH, 600, 500, Dpi);
            Assert.Equal(small - 200, tall);
        }

        [Fact]
        public void 옆으로만_옮기면_위아래_기준과_여백은_그대로()
        {
            // 화면을 거의 채운 패널(가운데가 아래쪽 반)을 옆으로만 옮겨도 위 기준을 유지한다
            var s = new HudSettings();
            int h = Placement.MaxHeight(s, WaH, Dpi);
            var (x, y) = Placement.Locate(s, WaX, WaY, WaW, WaH, 600, h, Dpi);
            Placement.FromBox(s, x - 300, y, 600, h, -300, 0, WaX, WaY, WaW, WaH, Dpi);
            Assert.Equal("top", s.AlignY);
            Assert.Equal(48, s.OffsetY);
            Assert.Equal("right", s.AlignX);
            Assert.Equal(48 + 200, s.OffsetX);
        }

        [Fact]
        public void 화면_절반보다_큰_패널을_내리면_위_기준으로_놓인_자리에_둔다()
        {
            var s = new HudSettings();
            Placement.FromBox(s, 2000, 500, 600, 1000, 0, 400, WaX, WaY, WaW, WaH, Dpi);
            Assert.Equal("top", s.AlignY);
            Assert.Equal(333, s.OffsetY);
            // 아래로 남은 만큼만 쓴다
            Assert.True(Placement.MaxHeight(s, WaH, Dpi) < 1000);
        }

        [Fact]
        public void 화면_밖으로_끌어도_안쪽에_붙인다()
        {
            var s = new HudSettings();
            Placement.FromBox(s, WaX - 300, -50, 600, 300, -2000, -100, WaX, WaY, WaW, WaH, Dpi);
            Assert.Equal("left", s.AlignX);
            Assert.Equal("top", s.AlignY);
            Assert.Equal(0, s.OffsetX);
            Assert.Equal(0, s.OffsetY);
        }

        [Fact]
        public void 위_기준_최대_높이는_아래쪽에_여백만_남긴다()
        {
            // 위에서 400(DIP) 떨어져 있으면 아래로 남은 만큼 쓰고, 끝에 24(DIP)를 남긴다
            var s = new HudSettings { AlignY = "top", OffsetY = 400 };
            Assert.Equal(WaH - 600 - 36, Placement.MaxHeight(s, WaH, Dpi));
            Assert.Equal(WaH - 72 - 36, Placement.MaxHeight(new HudSettings(), WaH, Dpi));
        }
    }
}

using Newtonsoft.Json;

namespace Parity
{
    internal class ParityData
    {
        public float JsonTime { get; set; }
        public int Color { get; set; }
        public int PosX { get; set; }
        public int PosY { get; set; }
        public bool IsForehand { get; set; }
        public bool ManuallyTagged { get; set; } = false;

        public ParityData(float jsonTime, int color, int posX, int posY, bool isForehand)
        {
            JsonTime = jsonTime;
            Color = color;
            PosX = posX;
            PosY = posY;
            IsForehand = isForehand;
        }
    }
}

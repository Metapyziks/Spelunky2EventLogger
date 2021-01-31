using System;

namespace Spelunky2AutoEditor
{
    public struct GameState
    {
        public const int MaxPlayers = 4;

        public byte loading;
        public bool ingame;
        public bool playing;
        public byte pause;
        public byte world;
        public byte level;
        public byte door;
        public ulong currentScore;
        public bool udjatEyeAvailable;
        public PlayerState player0;
    }

    public struct PlayerState
    {
        public byte life;
        public byte numBombs;
        public byte numRopes;
        public bool hasAnkh;
        public bool hasKapala;
        public bool isPoisoned;
        public bool isCursed;
    }

    class Program
    {
        static void Main(string[] args)
        {
            string inputVideoPath = null;
            string inputEventsPath = null;

            for (var i = 0; i < args.Length - 1; ++i)
            {
                var option = args[i];

                switch (option)
                {
                    case "-v":
                    case "--input-video":
                        inputVideoPath = args[i + 1];
                        break;

                    case "-e":
                    case "--input-events":
                        inputEventsPath = args[i + 1];
                        break;
                }
            }


        }
    }
}

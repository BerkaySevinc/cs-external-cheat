

using System.Runtime.InteropServices;

using BekoS;
using static Offsets.Offsets;



namespace CS_External_Cheat
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }


        private AdvancedProcess cs = new AdvancedProcess("csgo.exe");
        private async void Form1_Load(object sender, EventArgs e)
        {
            await cs.WaitForOpenAsync(3000);
            await cs.Memory.WaitForConnectAsync(3000);

            lblIsConnected.Text = "Connected: Yes";

            await Task.Run(Loop);
        }



        private void Loop()
        {
            var client = cs.Memory.GetModuleAddressByName("client.dll");

            var colorRed = new ColorStruct(0.8F, 0.0F, 0.1F);
            var colorBlue = new ColorStruct(0.0F, 0.3F, 0.7F);

            while (true)
            {
                // Sleep Before Each Update
                Thread.Sleep(1);

                // Get Local Player
                var localPlayer = cs.Memory.ReadPointer(client + dwLocalPlayer);
                if (localPlayer == UIntPtr.Zero) continue;

                int localTeam = cs.Memory.Read<int>(localPlayer + m_iTeamNum);
                int enemyTeam = localTeam == 2 ? 3 : 2;

                // Bhop
                /*
                int onGround = cs.Memory.Read<bool>(localPlayer + m_fFlags) ? 1 : 0;
                // If Space Key Down And On Ground Force Jump
                if (GetAsyncKeyState(0x20) < 0 && (onGround & (1 << 0)) == 1)
                    cs.Memory.Write<byte>(client + dwForceJump, 6);
                */

                var glowObjectManager = cs.Memory.ReadPointer(client + dwGlowObjectManager);

                // Loop Players
                for (UIntPtr i = 0; i <= 32; i++)
                {
                    // Get Player
                    var player = cs.Memory.ReadPointer(client + dwEntityList + i * 0x10);
                    if (player == UIntPtr.Zero) continue;

                    // Is Alive
                    bool isAlive = cs.Memory.Read<int>(player + m_lifeState) == 0;
                    if (!isAlive) continue;

                    int playerTeam = cs.Memory.Read<int>(player + m_iTeamNum);

                    var glowIndex = (UIntPtr)cs.Memory.Read<int>(player + m_iGlowIndex);

                    // If Enemy
                    if (playerTeam == enemyTeam)
                    {
                        // Glow Red
                        cs.Memory.Write(glowObjectManager + (glowIndex * 0x38) + 0x8, colorRed);

                        // Radar Spotted
                        cs.Memory.Write(player + m_bSpotted, true);
                    }
                    // If Teammate
                    else if (playerTeam == localTeam)
                    {
                        // Glow Blue
                        cs.Memory.Write(glowObjectManager + (glowIndex * 0x38) + 0x8, colorBlue);
                    }

                    // Activate Glow
                    cs.Memory.Write(glowObjectManager + (glowIndex * 0x38) + 0x28, true); // render
                    //cs.Memory.Write(glowObjectManager + (glowIndex * 0x38) + 0x29, false); // render
                }

            }
        }


        struct ColorStruct
        {
            public ColorStruct(float r, float g, float b, float a = 1.0F)
            {
                this.r = r;
                this.g = g;
                this.b = b;
                this.a = a;
            }

            public float r;
            public float g;
            public float b;
            public float a;
        }


        [DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
        private static extern short GetAsyncKeyState(int keyCode);

    }
}
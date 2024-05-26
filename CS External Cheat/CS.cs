using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BekoS;

using static Offsets.Offsets;


namespace CSProcess
{

    // Update() leri private yap
    // IOffsets objesi alsın ctor da

    // sadece kullanılan şeylerin memorylerini oku boşuna yormasın

    public class CS : AdvancedProcess
    {
        public Player? LocalPlayer { get; private set; }
        public Matrix? ViewMatrix { get; private set; }

        public Team LocalTeam { get; } = new Team();
        public Team EnemyTeam { get; } = new Team();


        private List<Player> Entities { get; } = new List<Player>();


        public event Action? Updated;


        private int updateInterval;
        public CS(int updateInterval) : base("csgo.exe")
        {
            this.updateInterval = updateInterval;
            Task.Run(() => Initialize());
        }



        #region Initialize

        private CancellationToken updateToken = new CancellationToken();
        private void Initialize()
        {
            // Wait For Open
            WaitForOpenAsync(3000).Wait();
            Memory.WaitForConnectAsync(3000).Wait();

            // Set Local Player
            LocalPlayer = new Player(this, $"client.dll+{dwLocalPlayer}");

            // Set View Matrix
            ViewMatrix = new Matrix(this, $"client.dll+{dwViewMatrix}");

            // Set Entities
            for (int i = 0; i < 64; i++)
            {
                var entity = new Player(this, $"client.dll+{dwEntityList + i * 0x10}");
                Entities.Add(entity);
            }

            // Update Loop
            Task.Run(() =>
            {
                while (Memory.IsConnected && !updateToken.IsCancellationRequested)
                {
                    // Update Local Player
                    LocalPlayer.Update();

                    // Update View Matrix
                    ViewMatrix.Update();

                    // Update Entities
                    foreach (Player player in Entities)
                    {
                        player.Update();
                        if (!player.IsExists) break;
                    }

                    // Update CSGO Object
                    Update();

                    // Trigger Event
                    Updated?.Invoke();

                    // Sleep
                    Thread.Sleep(updateInterval);
                }
            });
        }

        #endregion

        #region Update CSGO

        public void Update()
        {
            // Update Teams
            LocalTeam.Type = LocalPlayer!.Team;
            EnemyTeam.Type = LocalPlayer!.Team == TeamType.Terrorists ? TeamType.CounterTerrorists : TeamType.Terrorists;

            foreach (Player entity in Entities)
            {
                if (!entity.IsExists) break;

                bool isTeammate = entity.Team == LocalTeam.Type;
                bool isEnemy = entity.Team == EnemyTeam.Type;

                // If Teammate
                if (isTeammate)
                {
                    if (!LocalTeam.Players.Contains(entity)) LocalTeam.Players.Add(entity);
                    if (EnemyTeam.Players.Contains(entity)) EnemyTeam.Players.Remove(entity);
                }
                // If Enemy
                else if (isEnemy)
                {
                    if (LocalTeam.Players.Contains(entity)) LocalTeam.Players.Remove(entity);
                    if (!EnemyTeam.Players.Contains(entity)) EnemyTeam.Players.Add(entity);
                }
            }
        }

        #endregion


        #region Player

        public class Player
        {
            public bool IsExists { get; private set; }
            public bool IsRendered { get; private set; }

            public TeamType Team { get; private set; }

            private bool LifeState { get; set; }
            public bool IsAlive { get; private set; }

            public int Health { get; private set; }
            public int Armor { get; private set; }

            public float X { get; private set; }
            public float Y { get; private set; }
            public float Z { get; private set; }


            public UIntPtr MemoryAddress { get; private set; }


            private CS cs;
            private string pointer;
            public Player(CS cs, string address)
            {
                this.cs = cs;
                this.pointer = address;
            }

            public void Update()
            {
                // Is Exists
                MemoryAddress = cs.Memory.ReadPointer(pointer, false)!;
                IsExists = MemoryAddress != UIntPtr.Zero;
                if (!IsExists) return;

                Team = (TeamType)cs.Memory.Read<int>($"{MemoryAddress}+{m_iTeamNum}", false);

                IsRendered = !cs.Memory.Read<bool>($"{MemoryAddress}+{m_bDormant}", false);
                if (!IsRendered) return;

                LifeState = !cs.Memory.Read<bool>($"{MemoryAddress}+{m_lifeState}", false);

                IsAlive = LifeState && (Team == TeamType.Terrorists || Team == TeamType.CounterTerrorists);
                if (!IsAlive) return;

                Health = cs.Memory.Read<int>($"{MemoryAddress}+{m_iHealth}", false);
                Armor = cs.Memory.Read<int>($"{MemoryAddress}+{m_ArmorValue}", false);

                byte[] coordinates = cs.Memory.ReadBytes($"{MemoryAddress}+{m_vecOrigin}", 12, false)!;
                X = BitConverter.ToSingle(coordinates, 0);
                Y = BitConverter.ToSingle(coordinates, 4);
                Z = BitConverter.ToSingle(coordinates, 8);
            }


            public Rectangle? GetScreenRectangle(int screenWidth, int screenHeight)
            {
                var b = LocationToScreen(X, Y, Z, screenWidth, screenHeight);
                if (b is null) return null;

                var t = LocationToScreen(X, Y, Z + 58, screenWidth, screenHeight)!;
                if (t is null) return null;

                var bottom = (Point)b;
                var top = (Point)t;

                Point location = new Point(bottom.X - (bottom.Y - top.Y) / 4, top.Y);
                Size size = new Size((bottom.Y - top.Y) / 2, bottom.Y - top.Y);

                return new Rectangle(location, size);
            }

            public Point? GetScreenLocation(int screenWidth, int screenHeight)
            {
                return LocationToScreen(X, Y, Z + 42, screenWidth, screenHeight);
            }

            private Point? LocationToScreen(float x, float y, float z, int screenWidth, int screenHeight)
            {
                var viewMatrix = cs.ViewMatrix;
                if (viewMatrix is null) return null;

                float screenW = (viewMatrix.M41 * x) + (viewMatrix.M42 * y) + (viewMatrix.M43 * z) + viewMatrix.M44;

                // If Behind The Cam
                if (screenW <= 0.001F) return null;

                float screenX = (viewMatrix.M11 * x) + (viewMatrix.M12 * y) + (viewMatrix.M13 * z) + viewMatrix.M14;
                float screenY = (viewMatrix.M21 * x) + (viewMatrix.M22 * y) + (viewMatrix.M23 * z) + viewMatrix.M24;

                float camX = screenWidth / 2F;
                float camY = screenHeight / 2F;

                float X = camX + (camX * screenX / screenW);
                float Y = camY - (camY * screenY / screenW);

                return new Point((int)X, (int)Y);
            }
        }

        #endregion

        #region Team

        public class Team
        {
            public List<Player> Players { get; private set; } = new List<Player>();
            public TeamType Type { get; set; }

        }

        public enum TeamType
        {
            Unknown,
            Spectator,
            Terrorists,
            CounterTerrorists
        }

        #endregion

        #region View Matrix

        public class Matrix
        {
            public float
                M11, M12, M13, M14,
                M21, M22, M23, M24,
                M31, M32, M33, M34,
                M41, M42, M43, M44;

            private CS cs;
            private string pointer;
            public Matrix(CS cs, string address)
            {
                this.cs = cs;
                this.pointer = address;
            }

            public void Update()
            {
                byte[] viewMatrixData = cs.Memory.ReadBytes(pointer, 16 * 4, false)!;

                M11 = BitConverter.ToSingle(viewMatrixData, 0 * 4);
                M12 = BitConverter.ToSingle(viewMatrixData, 1 * 4);
                M13 = BitConverter.ToSingle(viewMatrixData, 2 * 4);
                M14 = BitConverter.ToSingle(viewMatrixData, 3 * 4);

                M21 = BitConverter.ToSingle(viewMatrixData, 4 * 4);
                M22 = BitConverter.ToSingle(viewMatrixData, 5 * 4);
                M23 = BitConverter.ToSingle(viewMatrixData, 6 * 4);
                M24 = BitConverter.ToSingle(viewMatrixData, 7 * 4);

                M31 = BitConverter.ToSingle(viewMatrixData, 8 * 4);
                M32 = BitConverter.ToSingle(viewMatrixData, 9 * 4);
                M33 = BitConverter.ToSingle(viewMatrixData, 10 * 4);
                M34 = BitConverter.ToSingle(viewMatrixData, 11 * 4);

                M41 = BitConverter.ToSingle(viewMatrixData, 12 * 4);
                M42 = BitConverter.ToSingle(viewMatrixData, 13 * 4);
                M43 = BitConverter.ToSingle(viewMatrixData, 14 * 4);
                M44 = BitConverter.ToSingle(viewMatrixData, 15 * 4);
            }
        }

        #endregion

    }

}

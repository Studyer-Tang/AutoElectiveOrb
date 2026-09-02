using System.Threading;
using System.Windows.Forms;

namespace AutoElectiveOrb
{
    internal static class Program
    {
        public static bool InspectMode { get; private set; }

        [System.STAThread]
        private static void Main(string[] args)
        {
            InspectMode = System.Array.Exists(args ?? new string[0], value => value == "--inspect");
            bool created;
            using (var mutex = new Mutex(true, "Local\\AutoElectiveOrb.Desktop", out created))
            {
                if (!created) return;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new OrbForm());
            }
        }
    }
}

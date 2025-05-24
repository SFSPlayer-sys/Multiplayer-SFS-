using System;
using System.IO;
using System.Threading;

namespace MultiplayerSFS.Server
{
    public class Program
    {
	    private const string CONFIG_FILENAME = "Multiplayer.cfg";
		/// <summary>
		/// Stops the server's config file from being saved or loaded.
		/// </summary>
		private static readonly bool DEV_MODE = false;

        private static void ShowStartupMessage()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("提示：输入 help 查看帮助信息");
            Console.ResetColor();
        }

        private static string GetDifficultyFromUser()
        {
            while (true)
            {
                Console.WriteLine("\n请选择世界难度：");
                Console.WriteLine("1. Normal (普通)");
                Console.WriteLine("2. Hard (困难)");
                Console.WriteLine("3. Realistic (真实)");
                Console.Write("请输入选项编号 (默认1): ");

                string input = Console.ReadLine()?.Trim();
                if (string.IsNullOrEmpty(input))
                {
                    return "Normal";
                }

                switch (input)
                {
                    case "1":
                        return "Normal";
                    case "2":
                        return "Hard";
                    case "3":
                        return "Realistic";
                    default:
                        Console.WriteLine("无效的选项，请重新输入！");
                        break;
                }
            }
        }

        private static void CreateDefaultWorld()
        {
            try
            {
                bool isNewWorld = !Directory.Exists("Sav");
                string difficulty = "Normal";

                if (isNewWorld)
                {
                    Console.WriteLine("\n检测到新世界，需要设置初始难度...");
                    difficulty = GetDifficultyFromUser();
                }

                // 创建主目录
                if (!Directory.Exists("Sav"))
                {
                    Directory.CreateDirectory("Sav");
                    Logger.Info("创建世界目录 Sav/", true);
                }
                if (!Directory.Exists("Sav/Persistent"))
                {
                    Directory.CreateDirectory("Sav/Persistent");
                    Logger.Info("创建世界目录 Sav/Persistent/", true);
                }

                // 创建 WorldSettings.txt
                if (!File.Exists("Sav/WorldSettings.txt"))
                {
                    File.WriteAllText("Sav/WorldSettings.txt", $@"{{
    ""difficulty"": {{
        ""difficulty"": ""{difficulty}""
    }}
}}");
                    Logger.Info($"创建世界配置文件 Sav/WorldSettings.txt (难度: {difficulty})", true);
                }

                // 创建 WorldState.txt
                if (!File.Exists("Sav/Persistent/WorldState.txt"))
                {
                    File.WriteAllText("Sav/Persistent/WorldState.txt", @"{
    ""worldTime"": 0.0
}");
                    Logger.Info("创建世界状态文件 Sav/Persistent/WorldState.txt", true);
                }

                // 创建 Rockets.txt
                if (!File.Exists("Sav/Persistent/Rockets.txt"))
                {
                    File.WriteAllText("Sav/Persistent/Rockets.txt", "[]");
                    Logger.Info("创建火箭数据文件 Sav/Persistent/Rockets.txt", true);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"创建默认世界文件时出错: {ex.Message}");
                throw;
            }
        }

		public static void Main()
		{
			try
			{
                ShowStartupMessage(); // 显示启动提示信息

                // 检查并创建默认世界文件
                CreateDefaultWorld();

				ServerSettings settings;
				if (DEV_MODE)
				{
					Logger.Info("开发模式已启用，使用默认配置...", true);
					settings = new ServerSettings();
					File.Delete(CONFIG_FILENAME);
				}
				else if (!File.Exists(CONFIG_FILENAME))
				{
					Logger.Info($"'{CONFIG_FILENAME}' 未找到配置文件，使用默认配置...", true);
					settings = new ServerSettings();
					File.WriteAllText(CONFIG_FILENAME, settings.Serialize());
				}
				else
				{
					Logger.Info($"从 '{CONFIG_FILENAME}' 加载服务器配置...", true);
					settings = ServerSettings.Deserialize(File.ReadAllText(CONFIG_FILENAME));
				}
				Server.Initialize(settings);
				ServerCommands.Initialize();
				// 启动命令监听线程
				new Thread(() =>
				{
					while (true)
					{
						var cmd = Console.ReadLine();
						if (!string.IsNullOrWhiteSpace(cmd))
						{
							ServerCommands.ExecuteCommand(cmd);
						}
					}
				})
				{ IsBackground = true }.Start();
				Server.Run();
			}
			catch (Exception e)
			{
				Logger.Error(e);
			}
		}
	}
}

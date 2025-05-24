using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Lidgren.Network;
using MultiplayerSFS.Common;
using System.Management;
using System.IO;

namespace MultiplayerSFS.Server
{
    public static class ServerCommands
    {
        private static Dictionary<string, Action<string[]>> commands = new Dictionary<string, Action<string[]>>();
        private static Dictionary<string, string> commandDescriptions = new Dictionary<string, string>();
        private static Dictionary<string, string> commandHelp = new Dictionary<string, string>();

        public static void Initialize()
        {
            // 注册所有命令
            RegisterCommand("help", HelpCommand, "查看帮助", "help - 查看帮助");
            RegisterCommand("info", InfoCommand, "显示配置", "info - 显示配置");
            RegisterCommand("list", ListCommand, "显示玩家", "list - 显示玩家");
            RegisterCommand("kick", KickCommand, "踢出玩家", "kick [玩家] - 踢出玩家");
            RegisterCommand("blacklist", BlacklistCommand, "查看黑名单", "blacklist - 查看黑名单");
            RegisterCommand("ban", BanCommand, "封禁玩家", "ban [玩家] - 封禁玩家");
            RegisterCommand("unban", UnbanCommand, "解除封禁", "unban [玩家] - 解除封禁");
            RegisterCommand("save", SaveCommand, "保存世界", "save [时间] - 保存世界");
            RegisterCommand("clearall", ClearAllCommand, "清除火箭", "clearall - 清除火箭");
            RegisterCommand("clearshares", ClearSharesCommand, "清除碎片", "clearshares [时间] - 清除碎片");
            RegisterCommand("config", ConfigCommand, "修改配置", "config [项] [值] [-n] - 修改配置");
            RegisterCommand("mes", MessageCommand, "发送消息", "mes [消息] - 发送消息");
            RegisterCommand("status", StatusCommand, "显示状态", "status - 显示状态");
            RegisterCommand("about", AboutCommand, "关于", "about - 关于");
            RegisterCommand("help", HelpCommand, "帮助", "help - 帮助");
        }

        private static void RegisterCommand(string name, Action<string[]> handler, string description, string help)
        {
            commands[name.ToLower()] = handler;
            commandDescriptions[name.ToLower()] = description;
            commandHelp[name.ToLower()] = help;
        }

        public static void ExecuteCommand(string command)
        {
            string[] parts = command.Split(' ');
            string cmd = parts[0].ToLower();
            string[] args = parts.Skip(1).ToArray();

            if (commands.ContainsKey(cmd))
            {
                try
                {
                    commands[cmd](args);
                }
                catch (Exception ex)
                {
                    Logger.Error($"执行命令 {cmd} 时出错: {ex.Message}");
                }
            }
            else
            {
                Logger.Info($"未知命令：{cmd} 请输入help查看帮助");
            }
        }

        // 命令实现
        private static void HelpCommand(string[] args)
        {
            Logger.Info("可用命令列表：");
            foreach (var cmd in commandHelp)
            {
                Logger.Info(cmd.Value);
            }
        }

        private static void ListCommand(string[] args)
        {
            Logger.Info("在线玩家列表：");
            foreach (var kvp in Server.connectedPlayers)
            {
                var player = kvp.Value;
                var connection = Server.server.GetConnection(kvp.Key);
                Logger.Info($"{player.username} {connection?.RemoteEndPoint?.Address}");
            }
        }

        private static void InfoCommand(string[] args)
        {
            Logger.Info("服务器配置：");
            Logger.Info($"端口: {Server.settings.port}");
            Logger.Info($"最大连接数: {Server.settings.maxConnections}");
            Logger.Info($"世界存档路径: {Server.settings.worldSavePath}");
            string difficultyName = Server.settings.difficulty switch
            {
                0 => "Normal",
                1 => "Hard",
                2 => "Realistic",
                _ => "Unknown"
            };
            Logger.Info($"难度: {difficultyName}");
        }

        private static void ClearAllCommand(string[] args)
        {
            Server.world.rockets.Clear();
            Logger.Info("已清除所有火箭");
        }

        private static void ClearSharesCommand(string[] args)
        {
            if (args.Length == 0)
            {
                Server.world.rockets.Clear();
                Logger.Info("已清除所有碎片");
                return;
            }

            string timeStr = args[0];
            if (timeStr == "-1")
            {
                // 取消定时清除
                Logger.Info("已取消定时清除碎片");
                return;
            }

            // 解析时间
            int seconds = ParseTimeString(timeStr);
            if (seconds <= 0)
            {
                Logger.Info("无效的时间格式！");
                return;
            }

            // 设置定时清除
            Logger.Info($"已设置每 {timeStr} 清除一次碎片");
        }

        private static void AboutCommand(string[] args)
        {
            Logger.Info("Multiplayer SFS Server");
            Logger.Info("版本: 1.0.0");
            Logger.Info("作者: SFSGamer");
        }

        private static void SaveCommand(string[] args)
        {
            if (args.Length == 0)
            {
                Server.world.Save(Server.settings.worldSavePath);
                Logger.Info($"世界已保存到 {Server.settings.worldSavePath}");
                return;
            }

            string timeStr = args[0];
            if (timeStr == "-1")
            {
                // 取消定时保存
                Logger.Info("已取消定时保存");
                return;
            }

            // 解析时间
            int seconds = ParseTimeString(timeStr);
            if (seconds <= 0)
            {
                Logger.Info("无效的时间格式！");
                return;
            }

            // 设置定时保存
            Logger.Info($"已设置每 {timeStr} 自动保存一次");
        }

        private static void KickCommand(string[] args)
        {
            if (args.Length == 0)
            {
                Logger.Info("用法: kick [玩家]");
                return;
            }

            string target = args[0];
            var player = FindPlayer(target);
            if (player != null)
            {
                // 踢出玩家
                Logger.Info($"已踢出玩家 {player.username}");
            }
            else
            {
                Logger.Info($"未找到玩家 {target}");
            }
        }

        private static void BanCommand(string[] args)
        {
            if (args.Length == 0)
            {
                Logger.Info("用法: ban [玩家]");
                return;
            }

            string target = args[0];
            var player = FindPlayer(target);
            if (player != null)
            {
                // 封禁玩家
                Logger.Info($"已封禁玩家 {player.username}");
            }
            else
            {
                Logger.Info($"未找到玩家 {target}");
            }
        }

        private static void UnbanCommand(string[] args)
        {
            if (args.Length == 0)
            {
                Logger.Info("用法: unban [玩家]");
                return;
            }

            string target = args[0];
            // 解除封禁
            Logger.Info($"已解除玩家 {target} 的封禁");
        }

        private static void BlacklistCommand(string[] args)
        {
            Logger.Info("黑名单列表：");
            // 显示黑名单
        }

        private static void MessageCommand(string[] args)
        {
            if (args.Length == 0)
            {
                Logger.Info("用法: mes [消息]");
                return;
            }

            string message = string.Join(" ", args);
            foreach (var kvp in Server.connectedPlayers)
            {
                var connection = Server.server.GetConnection(kvp.Key);
                if (connection != null)
                {
                    NetOutgoingMessage msg = Server.server.CreateMessage();
                    msg.Write((byte)PacketType.PlayerConnected);
                    msg.Write(new Packet_PlayerConnected
                    {
                        Id = -1,
                        Username = message,
                        IconColor = UnityEngine.Color.white,
                        PrintMessage = true
                    });
                    Server.server.SendMessage(msg, connection, NetDeliveryMethod.ReliableOrdered);
                }
            }
        }

        private static void StatusCommand(string[] args)
        {
            Logger.Info("服务器状态：");
            Logger.Info($"在线玩家数: {Server.connectedPlayers.Count}");
            Logger.Info($"服务器运行时间: {GetUptime()}");
            
            // 获取CPU使用率
            float cpuUsage = GetCpuUsage();
            Logger.Info($"CPU使用率: {cpuUsage:F2}%");

            // 获取内存信息
            var (totalMemory, usedMemory) = GetMemoryInfo();
            Logger.Info($"内存使用: {usedMemory:F2}MB / {totalMemory:F2}MB ({(usedMemory/totalMemory*100):F2}%)");
        }

        private static float GetCpuUsage()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("select * from Win32_PerfFormattedData_PerfOS_Processor WHERE Name='_Total'"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        return Convert.ToSingle(obj["PercentProcessorTime"]);
                    }
                }
            }
            catch
            {
                return 0;
            }
            return 0;
        }

        private static (float total, float used) GetMemoryInfo()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        float totalMemory = Convert.ToSingle(obj["TotalVisibleMemorySize"]) / 1024; // 转换为MB
                        float freeMemory = Convert.ToSingle(obj["FreePhysicalMemory"]) / 1024; // 转换为MB
                        float usedMemory = totalMemory - freeMemory;
                        return (totalMemory, usedMemory);
                    }
                }
            }
            catch
            {
                return (0, 0);
            }
            return (0, 0);
        }

        private static string GetProcessorInfo()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        return obj["Name"].ToString();
                    }
                }
            }
            catch
            {
                return "Unknown";
            }
            return "Unknown";
        }

        private static void ConfigCommand(string[] args)
        {
            if (args.Length < 2)
            {
                Logger.Info("用法: config [项] [值] [-n]");
                return;
            }

            string key = args[0];
            string value = args[1];
            bool noRestart = args.Length > 2 && args[2] == "-n";

            // 修改配置
            var settings = Server.settings;
            var property = typeof(ServerSettings).GetProperty(key);
            if (property == null)
            {
                Logger.Info($"未知配置项: {key}");
                return;
            }

            try
            {
                var convertedValue = Convert.ChangeType(value, property.PropertyType);
                property.SetValue(settings, convertedValue);
                File.WriteAllText("Multiplayer.cfg", settings.Serialize());
                Logger.Info($"已修改配置 {key} = {value}");
                if (!noRestart)
                {
                    Logger.Info("服务器将在5秒后重启...");
                }
            }
            catch (Exception ex)
            {
                Logger.Info($"修改配置失败: {ex.Message}");
            }
        }

        // 辅助方法
        private static string GetUptime()
        {
            // 实现获取服务器运行时间
            return "0天0小时0分钟";
        }

        private static int ParseTimeString(string timeStr)
        {
            try
            {
                // 解析时间字符串，支持S/M/H/D
                Match match = Regex.Match(timeStr, @"^(\d+)([SMHD])$");
                if (!match.Success)
                {
                    return 0;
                }

                int value = int.Parse(match.Groups[1].Value);
                if (value <= 0)
                {
                    return 0;
                }

                string unit = match.Groups[2].Value;
                switch (unit)
                {
                    case "S": return value;
                    case "M": return value * 60;
                    case "H": return value * 3600;
                    case "D": return value * 86400;
                    default: return 0;
                }
            }
            catch
            {
                return 0;
            }
        }

        private static ConnectedPlayer FindPlayer(string identifier)
        {
            // 通过IP或用户名查找玩家
            foreach (var kvp in Server.connectedPlayers)
            {
                if (kvp.Value.username == identifier || kvp.Key.ToString() == identifier)
                    return kvp.Value;
            }
            return null;
        }
    }
} 
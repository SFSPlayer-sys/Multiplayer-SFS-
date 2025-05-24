using System;
using System.Text;
using System.Reflection;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using SFS.WorldBase;

namespace MultiplayerSFS.Server
{
	public class ServerConfigVariable : Attribute
	{
		public readonly string[] Comment;

		public ServerConfigVariable(params string[] comment)
		{
			Comment = comment;
		}
	}

	public class ServerSettings
	{
		[ServerConfigVariable(
			"多人游戏世界存档文件夹的路径。"
		)]
		[JsonPropertyName("worldSavePath")]
		public string worldSavePath = "Sav";

		[ServerConfigVariable(
			"服务器使用的端口。通常不需要更改，因为这也是客户端加入菜单的默认端口。"
		)]
		[JsonPropertyName("port")]
		public int port = 9806;
		
		[ServerConfigVariable(
			"玩家加入多人游戏服务器所需的密码。", 
			"警告：留空将允许任何知道服务器IP地址的玩家加入！"
		)]
		[JsonPropertyName("password")]
		public string password = "";

		[ServerConfigVariable(
			"服务器允许的最大同时在线玩家数。"
		)]
		[JsonPropertyName("maxConnections")]
		public int maxConnections = 16;
		
		[ServerConfigVariable(
			"防止玩家使用已在服务器上使用的用户名加入。"
		)]
		[JsonPropertyName("blockDuplicatePlayerNames")]
		public bool blockDuplicatePlayerNames = false;
		
		[ServerConfigVariable(
			"客户端向服务器发送新的火箭更新包的时间间隔（毫秒）。",
			"建议保持默认值 - 更高的值会增加抖动，更低的值会增加客户端和服务器端的CPU/网络负载。",
			"每秒更新次数 = 1000 / updateRocketsPeriod"
		)]
		[JsonPropertyName("updateRocketsPeriod")]
		public double updateRocketsPeriod = 20;

		[ServerConfigVariable(
			"用于确定玩家是否应该获得附近火箭的'更新权限'的距离。",
			"应始终设置为高于游戏当前的（卸载）距离（约1.2 * 5000）。"
		)]
		[JsonPropertyName("loadRange")]
		public double loadRange = 7500;
		
		[ServerConfigVariable(
			"玩家在多人聊天中发送下一条消息的冷却时间（秒）。",
			"用于减少垃圾信息和类似问题。设置为0可禁用冷却。"
		)]
		[JsonPropertyName("chatMessageCooldown")]
		public double chatMessageCooldown = 3;

		[ServerConfigVariable(
			"游戏世界难度设置。",
			"0 = Normal (普通)",
			"1 = Hard (困难)",
			"2 = Realistic (真实)"
		)]
		[JsonPropertyName("difficulty")]
		public int difficulty = 0;  // 默认难度为普通

		public string Serialize()
		{
			var options = new JsonSerializerOptions
			{
				WriteIndented = true,
				Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
			};
			
			// 添加注释到JSON
			var json = JsonSerializer.Serialize(this, options);
			var comments = new StringBuilder();
			
			foreach (var property in GetType().GetProperties())
			{
				var attr = property.GetCustomAttribute<ServerConfigVariable>();
				if (attr != null)
				{
					comments.AppendLine($"// {string.Join("\n// ", attr.Comment)}");
				}
			}
			
			return comments.ToString() + json;
		}

		public static ServerSettings Deserialize(string input)
		{
			try
			{
				// 移除注释行
				var lines = input.Split('\n');
				var jsonLines = lines.Where(line => !line.TrimStart().StartsWith("//")).ToArray();
				var json = string.Join("\n", jsonLines);

				var options = new JsonSerializerOptions
				{
					WriteIndented = true,
					Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
				};
				
				var result = JsonSerializer.Deserialize<ServerSettings>(json, options);
				if (result == null)
				{
					throw new Exception("Failed to deserialize settings");
				}
				return result;
			}
			catch (Exception ex)
			{
				throw new Exception("Config deserialization failed", ex);
			}
		}
	}
}

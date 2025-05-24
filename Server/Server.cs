using System;
using System.Net;
using System.Linq;
using System.Collections.Generic;
using Lidgren.Network;
using MultiplayerSFS.Common;
using Color = UnityEngine.Color;

namespace MultiplayerSFS.Server
{
    public static class Server
	{
		public static NetServer server;
		public static ServerSettings settings;
		public static WorldState world;
		public static Dictionary<IPEndPoint, ConnectedPlayer> connectedPlayers;

		public static void Initialize(ServerSettings settings)
		{
			Server.settings = settings;
            NetPeerConfiguration npc = new NetPeerConfiguration("multiplayersfs")
            {
                Port = settings.port,
				MaximumConnections = settings.maxConnections,
            };
			npc.EnableMessageType(NetIncomingMessageType.StatusChanged);
			npc.EnableMessageType(NetIncomingMessageType.ConnectionApproval);
			npc.EnableMessageType(NetIncomingMessageType.ConnectionLatencyUpdated);
			npc.EnableMessageType(NetIncomingMessageType.VerboseDebugMessage);

			world = new WorldState(settings.worldSavePath);
			world.difficulty = (Difficulty.DifficultyType)settings.difficulty;
			connectedPlayers = new Dictionary<IPEndPoint, ConnectedPlayer>();

            server = new NetServer(npc);
			server.Start();
		}

		public static void Run()
		{
			try
			{
				Logger.Info($"SFS多人游戏服务器启动，正在监听端口{server.Port}...", true);
				
				while (true)
				{
					if (Listen())
                    {
                        UpdatePlayerAuthorities();
                    }
					// world.UpdateWorldTime(connectedPlayers.Values.Any(p => p.controlledRocket >= 0));
				}
			}
			catch (Exception e)
			{
				Logger.Error(e);
			}
		}

		/// <summary>
		/// Returns `true` if a refresh of the players' update authorities is required.
		/// </summary>
		static bool Listen()
		{
			NetIncomingMessage msg;
			bool requiresRefresh = false;
			while ((msg = server.ReadMessage()) != null)
			{
				switch (msg.MessageType)
				{
					case NetIncomingMessageType.StatusChanged:
						requiresRefresh |= OnStatusChanged(msg);
						break;
					case NetIncomingMessageType.ConnectionApproval:
						OnPlayerConnectionAttempt(msg);
						break;
					case NetIncomingMessageType.ConnectionLatencyUpdated:
						OnLatencyUpdated(msg);
						break;
					case NetIncomingMessageType.Data:
						requiresRefresh |= OnIncomingPacket(msg);
						break;

					case NetIncomingMessageType.DebugMessage:
					case NetIncomingMessageType.VerboseDebugMessage:
						Logger.Info($"Lidgren Debug - \"{msg.ReadString()}\".", true);
						break;
					case NetIncomingMessageType.WarningMessage:
						Logger.Warning($"Lidgren Warning - \"{msg.ReadString()}\".");
						break;
					case NetIncomingMessageType.ErrorMessage:
						Logger.Error($"Lidgren Error - \"{msg.ReadString()}\".");
						break;
					default:
						Logger.Warning($"未处理的消息类型: {msg.MessageType} - {msg.DeliveryMethod} - {msg.LengthBytes} 字节.");
						break;
				}
				server.Recycle(msg);
			}
			return requiresRefresh;
		}

		static ConnectedPlayer FindPlayer(NetConnection connection)
		{
			if (connectedPlayers.TryGetValue(connection.RemoteEndPoint, out ConnectedPlayer res))
				return res;
			return null;
		}

		static string FormatUsername(this string username)
		{
            return string.IsNullOrWhiteSpace(username) ? "???" : $"'{username}'";
        }

		static void SendPacketToPlayer(NetConnection connection, Packet packet, NetDeliveryMethod method = NetDeliveryMethod.ReliableOrdered)
		{
			// Logger.Debug($"Sending packet of type '{packet.Type}'.");
			NetOutgoingMessage msg = server.CreateMessage();
			msg.Write((byte) packet.Type);
			msg.Write(packet);
			server.SendMessage(msg, connection, method);
		}

		static void SendPacketToAll(Packet packet, NetConnection except = null, NetDeliveryMethod method = NetDeliveryMethod.ReliableOrdered)
		{
			// Logger.Debug($"Sending packet of type '{packet.Type}' to all.");
			NetOutgoingMessage msg = server.CreateMessage();
			msg.Write((byte) packet.Type);
			msg.Write(packet);
			server.SendToAll(msg, except, method, 0);
		}

		/// <summary>
		/// Returns `true` if a refresh of the players' update authorities is required.
		/// </summary>
		static bool OnStatusChanged(NetIncomingMessage msg)
		{
			NetConnectionStatus status = (NetConnectionStatus) msg.ReadByte();
			string reason = msg.ReadString();
			string playerName = FindPlayer(msg.SenderConnection)?.username.FormatUsername();
			Logger.Info($"{playerName} IP {msg.SenderEndPoint} 状态已更改为 {status} - \"{reason}\".");

			switch (status)
			{
				case NetConnectionStatus.Disconnected:
					OnPlayerDisconnect(msg.SenderConnection);
					return true;
				case NetConnectionStatus.Connected:
					OnPlayerSuccessfulConnect(msg.SenderConnection);
					return false;
				default:
					return false;
			}
		}

        static void OnPlayerConnectionAttempt(NetIncomingMessage msg)
		{
			Packet_JoinRequest request = msg.SenderConnection.RemoteHailMessage.Read<Packet_JoinRequest>();
            NetConnection connection = msg.SenderConnection;
			Logger.Info($"收到来自 {request.Username.FormatUsername()} @ {connection.RemoteEndPoint} 的加入请求.", true);

			string reason = "连接已批准!";
			if (connectedPlayers.Count >= settings.maxConnections && settings.maxConnections != 0)
			{
				reason = $"服务器已满 ({connectedPlayers.Count}/{settings.maxConnections}).";
				goto ConnectionDenied;
			}
			if (string.IsNullOrWhiteSpace(request.Username))
			{
				reason = $"用户名不能为空";
				goto ConnectionDenied;
			}
			if (settings.blockDuplicatePlayerNames && connectedPlayers.Values.Select(player => player.username).Contains(request.Username))
			{
				reason = $"用户名 '{request.Username}' 已被使用";
				goto ConnectionDenied;
			}
			if (request.Password != settings.password && settings.password != "")
			{
				reason = $"密码错误";
				goto ConnectionDenied;
			}

			Logger.Info($"批准加入请求，发送世界信息...", true);
			
            ConnectedPlayer newPlayer = new ConnectedPlayer(request.Username);
			connectedPlayers.Add(connection.RemoteEndPoint, newPlayer);

			NetOutgoingMessage joinResponse = server.CreateMessage();
			joinResponse.Write
			(
				new Packet_JoinResponse()
				{
					PlayerId = newPlayer.id,
					UpdateRocketsPeriod = settings.updateRocketsPeriod,
					ChatMessageCooldown = settings.chatMessageCooldown,
					WorldTime = world.WorldTime,
					Difficulty = world.difficulty,
				}
			);
			connection.Approve(joinResponse);
			return;

			ConnectionDenied:
				Logger.Info($"拒绝加入请求 - {reason}", true);
				connection.Deny(reason);
		}

		static void OnPlayerSuccessfulConnect(NetConnection connection)
		{
			ConnectedPlayer player = FindPlayer(connection);
			if (player == null)
			{
				Logger.Warning("缺少新玩家，发送加入响应时!");
				return;
			}

			SendPacketToAll
			(
				new Packet_PlayerConnected()
				{
					Id = player.id,
					Username = player.username,
					PrintMessage = true,
				},
				connection
			);
			foreach (KeyValuePair<int, RocketState> kvp in world.rockets)
			{
				SendPacketToPlayer
				(
					connection,
					new Packet_CreateRocket()
					{
						GlobalId = kvp.Key,
						Rocket = kvp.Value,
					}
				);
			}
			foreach (KeyValuePair<IPEndPoint, ConnectedPlayer> kvp in connectedPlayers)
			{
				SendPacketToPlayer
				(
					connection,
					new Packet_PlayerConnected()
					{
						Id = kvp.Value.id,
						Username = kvp.Value.username,
						IconColor = kvp.Value.iconColor,
						PrintMessage = false,
					}
				);
				SendPacketToPlayer
				(
					connection,
					new Packet_UpdatePlayerControl()
					{
						PlayerId = kvp.Value.id,
						RocketId = kvp.Value.controlledRocket,
					}
				);
			}
		}

		static void OnPlayerDisconnect(NetConnection connection)
        {
			ConnectedPlayer player = FindPlayer(connection);
            if (player != null)
			{
				SendPacketToAll(new Packet_PlayerDisconnected() { Id = player.id });
				connectedPlayers.Remove(connection.RemoteEndPoint);
			}
        }
		
		static void OnLatencyUpdated(NetIncomingMessage msg)
		{
			if (FindPlayer(msg.SenderConnection) is ConnectedPlayer player)
			{
				string username = player.username.FormatUsername();
				player.avgTripTime = msg.SenderConnection.AverageRoundtripTime;
				Logger.Info($"平均往返时间已更新为 {username} IP {msg.SenderEndPoint} - {1000 * player.avgTripTime}ms.");

				SendPacketToPlayer
				(
					msg.SenderConnection,
					new Packet_UpdateWorldTime()
					{
						WorldTime = world.WorldTime,
					}
				);
			}
		}

		static void UpdatePlayerAuthorities()
		{
			foreach (ConnectedPlayer player in connectedPlayers.Values)
			{
				player.updateAuthority.Clear();
			}

			// * No players are connected or controlling rockets.
			if (connectedPlayers.All(kvp => kvp.Value.controlledRocket == -1))
            {
                return;
            }

			int maxCount = 1;
			foreach (KeyValuePair<int, RocketState> kvp in world.rockets)
			{
				ConnectedPlayer bestPlayer = null;
				foreach (ConnectedPlayer player in connectedPlayers.Values)
				{
					if (player.updateAuthority.Count > maxCount)
                    {
                        maxCount = player.updateAuthority.Count;
                    }

					// * 玩家控制火箭时应始终具有该火箭的更新权限。
					if (player.controlledRocket == kvp.Key)
					{
						bestPlayer = player;
						break;
					}
                    if (world.rockets.TryGetValue(player.controlledRocket, out RocketState controlledRocket))
					{
						// * 玩家在火箭的加载范围内应具有更新权限。
						Double2 distance = controlledRocket.location.position - kvp.Value.location.position;
						if (distance.magnitude <= settings.loadRange)
                        {
                            // * 如果两个或多个玩家在火箭的加载范围内，则应将更新权限授予延迟最低的玩家。
                            if (bestPlayer == null || player.avgTripTime < bestPlayer.avgTripTime)
                            {
                                bestPlayer = player;
                            }
                        }

                        // * 所有其他火箭应在玩家之间分配。
                        // TODO: 可能有一种更好的方法来分配剩余的火箭，考虑到连接延迟。
                        // TODO: （目前它只是检查当前玩家的“授权”数量是否低于最高数量）
                        if (bestPlayer == null && player.updateAuthority.Count <= maxCount)
                        {
                            bestPlayer = player;
                        }
                    }

				}
				if (bestPlayer == null)
				{
					Logger.Error("bestPlayer 为 null!");
				}
				bestPlayer.updateAuthority.Add(kvp.Key);
			}

			foreach (KeyValuePair<IPEndPoint, ConnectedPlayer> kvp in connectedPlayers)
			{
				SendPacketToPlayer
				(
					server.GetConnection(kvp.Key),
					new Packet_UpdatePlayerAuthority()
					{
						RocketIds = kvp.Value.updateAuthority,
					}
				);
			}
		}

		/// <summary>
		/// 返回 `true` 如果需要刷新玩家的更新权限。
		/// </summary>
        static bool OnIncomingPacket(NetIncomingMessage msg)
        {
            PacketType type = (PacketType) msg.ReadByte();
            Logger.Debug($"收到类型为 '{type}' 的数据包.");

            switch (type)
			{
				case PacketType.UpdatePlayerControl:
					OnPacket_UpdatePlayerControl(msg);
					return true;
				case PacketType.UpdatePlayerColor:
                    OnPacket_UpdatePlayerColor(msg);
                    return false;
				case PacketType.SendChatMessage:
                    OnPacket_SendChatMessage(msg);
                    return false;
				case PacketType.CreateRocket:
					OnPacket_CreateRocket(msg);
					return true;
				case PacketType.DestroyRocket:
					OnPacket_DestroyRocket(msg);
					return true;
				case PacketType.UpdateRocket:
					OnPacket_UpdateRocket(msg);
					return false;
				case PacketType.DestroyPart:
					OnPacket_DestroyPart(msg);
                    return true;
				case PacketType.UpdateStaging:
					OnPacket_UpdateStaging(msg);
                    return true;
				case PacketType.UpdatePart_EngineModule:
					OnPacket_UpdatePart_EngineModule(msg);
                    return true;
				case PacketType.UpdatePart_WheelModule:
					OnPacket_UpdatePart_WheelModule(msg);
                    return true;
				case PacketType.UpdatePart_BoosterModule:
					OnPacket_UpdatePart_BoosterModule(msg);
                    return true;
				case PacketType.UpdatePart_ParachuteModule:
					OnPacket_UpdatePart_ParachuteModule(msg);
                    return true;
				case PacketType.UpdatePart_MoveModule:
                    OnPacket_UpdatePart_MoveModule(msg);
                    return true;
				case PacketType.UpdatePart_ResourceModule:
                    OnPacket_UpdatePart_ResourceModule(msg);
                    return true;
                case PacketType.UpdateWorldTime:
                    // 服务器不需要处理客户端发送的世界时间更新
                    return false;
				default:
                    Logger.Error($"未处理的数据包类型: {type}, {msg.LengthBytes} 字节.");
					return false;
			}
        }

		static void OnPacket_UpdatePlayerControl(NetIncomingMessage msg)
		{
			Packet_UpdatePlayerControl packet = msg.Read<Packet_UpdatePlayerControl>();
			if (FindPlayer(msg.SenderConnection) is ConnectedPlayer player)
			{
				if (player.id == packet.PlayerId)
				{
					player.controlledRocket = packet.RocketId;
					SendPacketToAll
					(
						packet,
						msg.SenderConnection
					);
				}
				else
				{
					Logger.Warning("尝试更新控制火箭时，玩家ID不正确!");
				}
			}
			else
			{
				Logger.Error("尝试更新控制火箭时，缺少连接的玩家!");
			}
		}

		static void OnPacket_UpdatePlayerColor(NetIncomingMessage msg)
        {
            Packet_UpdatePlayerColor packet = msg.Read<Packet_UpdatePlayerColor>();
			if (FindPlayer(msg.SenderConnection) is ConnectedPlayer player)
            {
                player.iconColor = packet.Color;
				SendPacketToAll(packet, msg.SenderConnection);
            }
        }

		static void OnPacket_SendChatMessage(NetIncomingMessage msg)
        {
            Packet_SendChatMessage packet = msg.Read<Packet_SendChatMessage>();
			SendPacketToAll(packet, msg.SenderConnection);
        }

		static void OnPacket_CreateRocket(NetIncomingMessage msg)
		{
			Packet_CreateRocket packet = msg.Read<Packet_CreateRocket>();
			if (world.rockets.ContainsKey(packet.GlobalId))
            {
				// Logger.Debug($"existing: {packet.Rocket.parts.Count}");
                world.rockets[packet.GlobalId] = packet.Rocket;
            	SendPacketToAll(packet, msg.SenderConnection);
            }
            else
            {
				// Logger.Debug($"new: {packet.Rocket.parts.Count}");
                packet.GlobalId = world.rockets.InsertNew(packet.Rocket);
            	SendPacketToAll(packet);
				if (FindPlayer(msg.SenderConnection) is ConnectedPlayer player)
				{
					player.updateAuthority.Add(packet.GlobalId);
					SendPacketToPlayer
					(
						msg.SenderConnection, new Packet_UpdatePlayerAuthority()
						{
							RocketIds = player.updateAuthority,
						}
					);
				}
            }

		}

		static void OnPacket_DestroyRocket(NetIncomingMessage msg)
		{
			Packet_DestroyRocket packet = msg.Read<Packet_DestroyRocket>();
			if (world.rockets.Remove(packet.Id))
            {
                SendPacketToAll(packet, msg.SenderConnection);
            }
		}

		static void OnPacket_UpdateRocket(NetIncomingMessage msg)
		{
			Packet_UpdateRocket packet = msg.Read<Packet_UpdateRocket>();
			if (world.rockets.TryGetValue(packet.Id, out RocketState state))
			{
				state.UpdateRocket(packet);
				SendPacketToAll(packet, msg.SenderConnection);
			}
		}

		static void OnPacket_DestroyPart(NetIncomingMessage msg)
		{
			Packet_DestroyPart packet = msg.Read<Packet_DestroyPart>();
			if (world.rockets.TryGetValue(packet.RocketId, out RocketState state))
			{
				if (state.RemovePart(packet.PartId))
					SendPacketToAll(packet, msg.SenderConnection);
			}
		}

		static void OnPacket_UpdateStaging(NetIncomingMessage msg)
		{
			Packet_UpdateStaging packet = msg.Read<Packet_UpdateStaging>();
			if (world.rockets.TryGetValue(packet.RocketId, out RocketState state))
			{
				state.stages = packet.Stages;
				SendPacketToAll(packet, msg.SenderConnection);
			}
		}

		static void OnPacket_UpdatePart_EngineModule(NetIncomingMessage msg)
		{
			Packet_UpdatePart_EngineModule packet = msg.Read<Packet_UpdatePart_EngineModule>();
			if (world.rockets.TryGetValue(packet.RocketId, out RocketState state))
			{
				if (state.parts.TryGetValue(packet.PartId, out PartState part))
				{
					part.part.TOGGLE_VARIABLES["engine_on"] = packet.EngineOn;
					SendPacketToAll(packet, msg.SenderConnection);
				}
			}
		}

		static void OnPacket_UpdatePart_WheelModule(NetIncomingMessage msg)
		{
			Packet_UpdatePart_WheelModule packet = msg.Read<Packet_UpdatePart_WheelModule>();
			if (world.rockets.TryGetValue(packet.RocketId, out RocketState state))
			{
				if (state.parts.TryGetValue(packet.PartId, out PartState part))
				{
					part.part.TOGGLE_VARIABLES["wheel_on"] = packet.WheelOn;
					SendPacketToAll(packet, msg.SenderConnection);
				}
			}
		}

		static void OnPacket_UpdatePart_BoosterModule(NetIncomingMessage msg)
		{
			Packet_UpdatePart_BoosterModule packet = msg.Read<Packet_UpdatePart_BoosterModule>();
			if (world.rockets.TryGetValue(packet.RocketId, out RocketState state))
			{
				if (state.parts.TryGetValue(packet.PartId, out PartState part))
				{
					// TODO: 火箭模块似乎不保存它们的开关状态？（至少RA复古包是这样）
					// TODO: 我猜这就是为什么它们在加载保存时激活后会无限推力？
					// TODO: 无论如何，我目前无法将它们的“预热”状态或推力输出保存到世界状态中。
					// TODO: 火箭模块只能通过RA复古包获得，所以现在无关紧要。
					part.part.NUMBER_VARIABLES["fuel_percent"] = packet.FuelPercent;
					SendPacketToAll(packet, msg.SenderConnection);
				}
			}
		}

		static void OnPacket_UpdatePart_ParachuteModule(NetIncomingMessage msg)
		{
			Packet_UpdatePart_ParachuteModule packet = msg.Read<Packet_UpdatePart_ParachuteModule>();
			if (world.rockets.TryGetValue(packet.RocketId, out RocketState state))
			{
				if (state.parts.TryGetValue(packet.PartId, out PartState part))
				{
					part.part.NUMBER_VARIABLES["animation_state"] = packet.State;
					part.part.NUMBER_VARIABLES["deploy_state"] = packet.TargetState;
					SendPacketToAll(packet, msg.SenderConnection);
				}
			}
		}

		static void OnPacket_UpdatePart_MoveModule(NetIncomingMessage msg)
		{
			Packet_UpdatePart_MoveModule packet = msg.Read<Packet_UpdatePart_MoveModule>();
			if (world.rockets.TryGetValue(packet.RocketId, out RocketState state))
			{
				if (state.parts.TryGetValue(packet.PartId, out PartState part))
				{
					part.part.NUMBER_VARIABLES["state"] = packet.Time;
					part.part.NUMBER_VARIABLES["state_target"] = packet.TargetTime;
					SendPacketToAll(packet, msg.SenderConnection);
				}
			}
		}

		static void OnPacket_UpdatePart_ResourceModule(NetIncomingMessage msg)
		{
			Packet_UpdatePart_ResourceModule packet = msg.Read<Packet_UpdatePart_ResourceModule>();
			if (world.rockets.TryGetValue(packet.RocketId, out RocketState state))
			{
				bool foundPart = false;
				foreach (int partId in packet.PartIds)
                {
                    if (state.parts.TryGetValue(partId, out PartState partState))
                    {
                        // TODO! 这些保存变量名称可能与非原版部件不同，但目前我不知道如何正确获取它们。
                        // TODO! 我可能需要某种注册表，将部件的名称和模块变量名称与它们的保存变量名称关联起来。
                        partState.part.NUMBER_VARIABLES["fuel_percent"] = packet.ResourcePercent;
						foundPart = true;
                    }
                }
				if (foundPart)
				{
					SendPacketToAll(packet, msg.SenderConnection);
				}
			}
		}
	}

	public class ConnectedPlayer
	{
		public int id;
		public string username;
		public Color iconColor;
		public float avgTripTime;

		public int controlledRocket;
		public HashSet<int> updateAuthority;

		static readonly Random colorRandom = new Random();
		static Color GetRandomColor()
		{
			float rand = (float) Math.Round(100 * colorRandom.NextDouble());
			return Color.HSVToRGB(rand / 100, 1, 1);
		}

		public ConnectedPlayer(string playerName)
		{
            id = Server.connectedPlayers.Select(kvp => kvp.Value.id).ToHashSet().InsertNew();
			username = playerName;
			iconColor = GetRandomColor();
			avgTripTime = 0;
			controlledRocket = -1;
			updateAuthority = new HashSet<int>();
		}
	}
}
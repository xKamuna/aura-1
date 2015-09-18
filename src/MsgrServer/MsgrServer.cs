// Copyright (c) Aura development team - Licensed under GNU GPL
// For more information, see license file in the main folder

using Aura.Mabi.Network;
using Aura.Msgr.Database;
using Aura.Msgr.Network;
using Aura.Msgr.Util;
using Aura.Shared;
using Aura.Shared.Util;
using Aura.Shared.Util.Commands;
using System;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace Aura.Msgr
{
	public class MsgrServer : ServerMain
	{
		public readonly static MsgrServer Instance = new MsgrServer();

		private bool _running = false;

		/// <summary>
		/// Instance of the actual server component.
		/// </summary>
		// TODO: Our naming sucks, rename "servers" to connection managers or
		//   something, rename "clients" to connections.
		private MsgrServerServer Server { get; set; }

		/// <summary>
		/// Database
		/// </summary>
		public MsgrDb Database { get; private set; }

		/// <summary>
		/// Configuration
		/// </summary>
		public MsgrConf Conf { get; private set; }

		/// <summary>
		/// Msgr's packet handlers
		/// </summary>
		public MsgrServerHandlers PacketHandlerManager { get; private set; }

		/// <summary>
		/// Initializes msgr server.
		/// </summary>
		private MsgrServer()
		{
			this.Database = new MsgrDb();
			this.Conf = new MsgrConf();
			this.Server = new MsgrServerServer();

			this.PacketHandlerManager = new MsgrServerHandlers();
			this.PacketHandlerManager.AutoLoad();
			this.Server.Handlers = this.PacketHandlerManager;
		}

		public void Run()
		{
			if (_running)
				throw new Exception("Server is already running.");
			_running = true;

			CliUtil.WriteHeader("Msgr Server", ConsoleColor.DarkCyan);
			CliUtil.LoadingTitle();

			this.NavigateToRoot();

			// Conf
			this.LoadConf(this.Conf = new MsgrConf());

			// Database
			this.InitDatabase(this.Database = new MsgrDb(), this.Conf);

			// Start
			this.Server.Start(this.Conf.Msgr.Port);

			var ws = new WebSocketServer("ws://127.0.0.1:8181");
			ws.AddWebSocketService<MsgrBehavior>("/");
			ws.Start();

			CliUtil.RunningTitle();

			var cmd = new ConsoleCommands();
			cmd.Wait();
		}
	}

	public class MsgrBehavior : WebSocketBehavior
	{
		protected override void OnOpen()
		{
			Shared.Util.Log.Debug("Connection opened.");
		}

		protected override void OnMessage(MessageEventArgs e)
		{
			//Shared.Util.Log.Debug("Msg: " + e.Data);
			Shared.Util.Log.Debug("RawMsg: " + BitConverter.ToString(e.RawData));

			var packet = new Packet(e.RawData, 0);
			Shared.Util.Log.Debug(packet);

			packet = new Packet(Op.Msgr.LoginR, 0);
			packet.PutByte(true);
			packet.PutInt(0x12345678);

			this.Send(packet.Build());
		}

		protected override void OnClose(CloseEventArgs e)
		{
			Shared.Util.Log.Debug("Connection closed.");
		}

		protected override void OnError(ErrorEventArgs e)
		{
			Shared.Util.Log.Debug("Error: " + e.Message);
		}
	}
}

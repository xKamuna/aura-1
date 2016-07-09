// Copyright (c) Aura development team - Licensed under GNU GPL
// For more information, see license file in the main folder

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aura.Shared.Network
{
	public class TcpBuffer
	{
		// Largest known packet is composing on R1, up to ~3700 bytes.
		private const int BufferDefaultSize = 4096;

		public byte[] Front;
		public byte[] Back;
		public int Remaining;
		public int Ptr;

		public TcpBuffer()
		{
			this.Front = new byte[BufferDefaultSize];
			this.Back = new byte[BufferDefaultSize];
		}
	}
}

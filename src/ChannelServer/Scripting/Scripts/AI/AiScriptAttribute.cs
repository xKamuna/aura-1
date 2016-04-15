// Copyright (c) Aura development team - Licensed under GNU GPL
// For more information, see license file in the main folder

using System;

namespace Aura.Channel.Scripting.Scripts.AI
{
	/// <summary>
	/// Attribute for AI scripts, to specify which races the script is for.
	/// </summary>
	public class AiScriptAttribute : Attribute
	{
		/// <summary>
		/// List of AI names
		/// </summary>
		public string[] Names { get; private set; }

		/// <summary>
		/// New attribute
		/// </summary>
		/// <param name="names"></param>
		public AiScriptAttribute(params string[] names)
		{
			this.Names = names;
		}
	}
}

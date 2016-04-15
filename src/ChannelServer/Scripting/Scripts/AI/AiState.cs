// Copyright (c) Aura development team - Licensed under GNU GPL
// For more information, see license file in the main folder

namespace Aura.Channel.Scripting.Scripts.Ai
{
	public enum AiState
	{
		/// <summary>
		/// Doing nothing
		/// </summary>
		Idle,

		/// <summary>
		/// Doing nothing, but noticed a potential target
		/// </summary>
		Aware,

		/// <summary>
		/// Watching target (!)
		/// </summary>
		Alert,

		/// <summary>
		/// Aggroing target (!!)
		/// </summary>
		Aggro,

		/// <summary>
		/// Likes target
		/// </summary>
		Love,
	}
}

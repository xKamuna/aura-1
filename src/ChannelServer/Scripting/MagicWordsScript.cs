﻿// Copyright (c) Aura development team - Licensed under GNU GPL
// For more information, see license file in the main folder

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Aura.Channel.World.Entities;
using Aura.Mabi.Const;
using Aura.Data;
using Aura.Shared.Util;
using Aura.Channel.Network.Sending;

namespace Aura.Channel.Scripting
{
	/// <summary>
	/// Function calls, stored in item meta data.
	/// </summary>
	/// <remarks>
	/// Nothing more than 1-2 function calls, or something that looks alike,
	/// stored in the meta data of sealed books. Usually related to adding
	/// or training of skills, but some also give keywords or stats.
	/// </remarks>
	public class MagicWordsScript
	{
		private string _script;
		private List<MagicWordsFunction> _calls;

		/// <summary>
		/// New empty script
		/// </summary>
		public MagicWordsScript()
		{
			_calls = new List<MagicWordsFunction>();
		}

		/// <summary>
		/// New script from string
		/// </summary>
		/// <param name="script"></param>
		public MagicWordsScript(string script)
		{
			_script = script;
			_calls = new List<MagicWordsFunction>();

			this.LoadCode(script);
		}

		/// <summary>
		/// Loads script code.
		/// </summary>
		/// <param name="script"></param>
		public void LoadCode(string script)
		{
			if (string.IsNullOrWhiteSpace(script)) return;

			// TODO: Parse properly
			foreach (Match call in Regex.Matches(script, @"(?<function>\w+)\s*\(\s*(?<arg1>[^,\)]+)?\s*(,\s*(?<arg2>[^,\)]+)\s*)?(,\s*(?<arg3>[^,\)]+)\s*)?(,\s*(?<arg4>[^,\)]+)\s*)?\)", RegexOptions.Compiled))
			{
				var function = new MagicWordsFunction();
				function.Name = call.Groups["function"].Value;

				if (!string.IsNullOrWhiteSpace(call.Groups["arg1"].Value)) function.Arguments.Add(call.Groups["arg1"].Value);
				if (!string.IsNullOrWhiteSpace(call.Groups["arg2"].Value)) function.Arguments.Add(call.Groups["arg2"].Value);
				if (!string.IsNullOrWhiteSpace(call.Groups["arg3"].Value)) function.Arguments.Add(call.Groups["arg3"].Value);
				if (!string.IsNullOrWhiteSpace(call.Groups["arg4"].Value)) function.Arguments.Add(call.Groups["arg4"].Value);

				_calls.Add(function);
			}
		}

		/// <summary>
		/// Runs script
		/// </summary>
		/// <param name="creature"></param>
		/// <param name="item"></param>
		public void Run(Creature creature, Item item)
		{
			foreach (var function in _calls)
			{
				switch (function.Name)
				{
					// trainskill(x,y)
					// Trains skill x's last condition on rank y.
					// Most skills only require reading a book to advance,
					// but Cooking has more conditions, and the book that can be
					// used on each rank is always the last trainable condition.
					case "trainskill":
						{
							var skillid = (SkillId)function.GetArgument<ushort>(0);
							var rank = (SkillRank)function.GetArgument<byte>(1);

							// Cancel if creature doesn't have the skill with
							// the correct rank.
							var skill = creature.Skills.Get(skillid);
							if (skill == null || skill.Info.Rank != rank)
								break;

							// Find the last trainable condition
							var found = false;
							for (int i = 8; i >= 0; --i)
							{
								if (skill.RankData.Conditions[i].Count > 0)
								{
									skill.Train(i + 1);
									found = true;
									break;
								}
							}

							if (!found)
								throw new Exception("No trainable condition found for " + skillid + " @ " + rank + ".");

							break;
						}

					// knowskill(x)
					// Gives skill x on novice rank?
					case "knowskill":
						{
							var skillid = (SkillId)function.GetArgument<ushort>(0);

							if (!creature.Skills.Has(skillid))
								creature.Skills.Give(skillid, SkillRank.Novice);
							break;
						}

					// openskill(x)
					// "Opens" the skill x for actual use, aka training the
					// first condition on novice rank?
					// Or maybe it grants rank F instantly?
					case "openskill":
						{
							var skillid = (SkillId)function.GetArgument<ushort>(0);

							if (creature.Skills.Is(skillid, SkillRank.Novice))
								creature.Skills.Train(skillid, 1);
							break;
						}

					// addskill(x,y)
					// Give skill x with rank y?
					case "addskill":
						{
							var skillid = (SkillId)function.GetArgument<ushort>(0);
							var rank = (SkillRank)function.GetArgument<byte>(1);

							if (!creature.Skills.Has(skillid, rank))
								creature.Skills.Give(skillid, rank);
							break;
						}

					// addkeyword(x)
					// Adds keyword.
					case "addkeyword":
						{
							creature.Keywords.Give(function.GetArgument<string>(0));
							break;
						}

					// erasekeyword(x)
					// deletekeyword(x)
					// Removes keyword? Maybe one "disables" it somehow?
					case "erasekeyword":
					case "deletekeyword":
						{
							creature.Keywords.Remove(function.GetArgument<string>(0));
							break;
						}

					// modify(x,y)
					// Modifies given stat.
					case "modify":
						{
							// Only allow this once per item id
							if (creature.Vars.Perm["StatModify_" + item.Info.Id] != null)
								break;

							var stat = function.GetArgument<string>(0);
							var modifier = function.GetArgument<int>(1);
							switch (stat)
							{
								case "str": creature.StrBonus += modifier; break;
								case "int": creature.IntBonus += modifier; break;
								case "dex": creature.DexBonus += modifier; break;
								case "will": creature.WillBonus += modifier; break;
								case "luck": creature.LuckBonus += modifier; break;
								default: Log.Warning("MagicWordsScript: Unknown modify stat '{0}'.", stat); continue;
							}

							creature.Vars.Perm["StatModify_" + item.Info.Id] = true;

							Send.StatUpdateDefault(creature);
							if (modifier > 0)
								Send.AcquireInfo(creature, stat, modifier);
							break;
						}

					// domint(x)
					// Executes given mint script on server.
					case "domint":
						{
							var scriptName = function.GetArgument<string>(0);
							Log.Unimplemented("MagicWordsScript: domint call for '{0}'.", scriptName);
							break;
						}

					default:
						throw new MissingMethodException("MagicWordsScript.Run: Unknown function '" + function.Name + "'.");
				}
			}
		}

		/// <summary>
		/// Represents a function in a script
		/// </summary>
		private class MagicWordsFunction
		{
			public string Name { get; set; }
			public List<string> Arguments { get; private set; }

			public MagicWordsFunction()
			{
				this.Arguments = new List<string>();
			}

			/// <summary>
			/// Returns argument number x, starting at 0.
			/// </summary>
			/// <typeparam name="T"></typeparam>
			/// <param name="index"></param>
			/// <returns></returns>
			public T GetArgument<T>(int index)
			{
				if (index < 0 || index > this.Arguments.Count - 1)
					throw new ArgumentException("MagicWordsFunction.GetArgument: Missing argument '" + index.ToString() + "' for '" + this.Name + "'.");

				var type = typeof(T);
				var result = (T)Convert.ChangeType(this.Arguments[index], type);
				if (result == null)
					throw new ArgumentException("MagicWordsFunction.GetArgument: Invalid argument '" + this.Arguments[index] + "' for '" + this.Name + "', expected " + type.Name + ".");

				return result;
			}
		}
	}
}

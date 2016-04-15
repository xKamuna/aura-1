// Copyright (c) Aura development team - Licensed under GNU GPL
// For more information, see license file in the main folder

using Aura.Channel.Network.Sending;
using Aura.Channel.Skills;
using Aura.Channel.Skills.Base;
using Aura.Channel.Skills.Combat;
using Aura.Channel.Skills.Life;
using Aura.Channel.World;
using Aura.Channel.World.Entities;
using Aura.Data;
using Aura.Mabi;
using Aura.Mabi.Const;
using Aura.Shared.Network;
using Aura.Shared.Scripting.Scripts;
using Aura.Shared.Util;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;

namespace Aura.Channel.Scripting.Scripts.Ai
{
	public abstract partial class AiScript : IScript, IDisposable
	{
		// Official heartbeat while following a target seems
		// to be about 100-200ms?

		protected int MinHeartbeat = 50; // ms
		protected int IdleHeartbeat = 250; // ms
		protected int AggroHeartbeat = 50; // ms

		// Maintenance
		protected Timer _heartbeatTimer;
		protected int _heartbeat;
		protected double _timestamp;
		protected DateTime _lastBeat;
		protected bool _active;
		protected DateTime _minRunTime;
		private bool _inside = false;
		private int _stuckTestCount = 0;

		protected Random _rnd;
		protected AiState _state;
		protected IEnumerator _curAction;
		protected Creature _newAttackable;

		protected Dictionary<AiState, Dictionary<AiEventType, Dictionary<SkillId, Func<IEnumerable>>>> _reactions;

		// Heartbeat cache
		protected IList<Creature> _playersInRange;

		// Settings
		protected int _aggroRadius, _aggroMaxRadius;
		protected int _visualRadius;
		protected double _visualRadian;
		protected TimeSpan _alertDelay, _aggroDelay, _hateBattleStanceDelay, _hateOverTimeDelay;
		protected DateTime _awareTime, _alertTime;
		protected AggroLimit _aggroLimit;
		protected Dictionary<string, string> _hateTags, _loveTags, _doubtTags;
		protected bool _hatesBattleStance;
		protected int _maxDistanceFromSpawn;

		// Misc
		private int _switchRandomN, _switchRandomM;

		/// <summary>
		/// Creature controlled by AI.
		/// </summary>
		public Creature Creature { get; protected set; }

		/// <summary>
		/// List of random phrases
		/// </summary>
		public List<string> Phrases { get; protected set; }

		/// <summary>
		/// Returns state of the AI.
		/// </summary>
		public AiState State { get { return _state; } }

		/// <summary>
		/// Initializes AI.
		/// </summary>
		protected AiScript()
		{
			this.Phrases = new List<string>();

			_lastBeat = DateTime.MinValue;
			_heartbeat = IdleHeartbeat;
			_heartbeatTimer = new Timer(this.Heartbeat, null, -1, -1);

			_rnd = new Random(RandomProvider.Get().Next());
			_reactions = new Dictionary<AiState, Dictionary<AiEventType, Dictionary<SkillId, Func<IEnumerable>>>>();
			_reactions[AiState.Idle] = new Dictionary<AiEventType, Dictionary<SkillId, Func<IEnumerable>>>();
			_reactions[AiState.Aware] = new Dictionary<AiEventType, Dictionary<SkillId, Func<IEnumerable>>>();
			_reactions[AiState.Alert] = new Dictionary<AiEventType, Dictionary<SkillId, Func<IEnumerable>>>();
			_reactions[AiState.Aggro] = new Dictionary<AiEventType, Dictionary<SkillId, Func<IEnumerable>>>();
			_reactions[AiState.Love] = new Dictionary<AiEventType, Dictionary<SkillId, Func<IEnumerable>>>();

			_state = AiState.Idle;
			_aggroRadius = 500;
			_aggroMaxRadius = 3000;
			_visualRadius = 900;
			_visualRadian = 90;
			_alertDelay = TimeSpan.FromMilliseconds(6000);
			_aggroDelay = TimeSpan.FromMilliseconds(500);
			_hateOverTimeDelay = TimeSpan.FromDays(365);
			_hateBattleStanceDelay = TimeSpan.FromMilliseconds(3000);
			_hateTags = new Dictionary<string, string>();
			_loveTags = new Dictionary<string, string>();
			_doubtTags = new Dictionary<string, string>();

			_maxDistanceFromSpawn = 3000;

			_aggroLimit = AggroLimit.One;
		}

		/// <summary>
		/// Disables heartbeat timer.
		/// </summary>
		public void Dispose()
		{
			_heartbeatTimer.Change(-1, -1);
			_heartbeatTimer.Dispose();
			_heartbeatTimer = null;
		}

		/// <summary>
		/// Called when script is initialized after loading it.
		/// </summary>
		/// <returns></returns>
		public bool Init()
		{
			var attr = this.GetType().GetCustomAttribute<AiScriptAttribute>();
			if (attr == null)
			{
				Log.Error("AiScript.Init: Missing AiScript attribute.");
				return false;
			}

			foreach (var name in attr.Names)
				ChannelServer.Instance.ScriptManager.AiScripts.Add(name, this.GetType());

			return true;
		}

		/// <summary>
		/// Starts AI.
		/// </summary>
		public void Activate(double minRunTime)
		{
			if (!_active && _heartbeatTimer != null)
			{
				_active = true;
				_minRunTime = DateTime.Now.AddMilliseconds(minRunTime);
				_heartbeatTimer.Change(_heartbeat, _heartbeat);
			}
		}

		/// <summary>
		/// Pauses AI.
		/// </summary>
		public void Deactivate()
		{
			if (_active && _heartbeatTimer != null)
			{
				_active = false;
				_curAction = null;
				_heartbeatTimer.Change(-1, -1);
			}
		}

		/// <summary>
		/// Sets AI's creature.
		/// </summary>
		/// <param name="creature"></param>
		public void Attach(Creature creature)
		{
			this.Creature = creature;
			this.Creature.Death += OnDeath;
		}

		/// <summary>
		/// Unsets AI's creature.
		/// </summary>
		/// <param name="creature"></param>
		public void Detach()
		{
			var npc = this.Creature as NPC;
			if (npc == null || npc.AI == null)
				return;

			npc.AI.Dispose();
			npc.Death -= OnDeath;
			npc.AI = null;
			this.Creature = null;
		}

		/// <summary>
		/// Main "loop".
		/// </summary>
		/// <param name="state"></param>
		private void Heartbeat(object state)
		{
			if (this.Creature == null || this.Creature.Region == Region.Limbo)
				return;

			// Skip tick if the previous one is still on.
			if (_inside)
			{
				if (++_stuckTestCount == 10)
					Log.Warning("AiScript.Heartbeat: {0} stuck?", this.GetType().Name);
				return;
			}

			_inside = true;
			_stuckTestCount = 0;
			try
			{
				var now = this.UpdateTimestamp();
				var pos = this.Creature.GetPosition();

				// Stop if no players in range
				_playersInRange = this.Creature.Region.GetPlayersInRange(pos);
				if (_playersInRange.Count == 0 && now > _minRunTime)
				{
					this.Deactivate();
					this.Reset();
					return;
				}

				if (this.Creature.IsDead)
					return;

				this.SelectState();

				// Select and run state
				var prevAction = _curAction;
				if (_curAction == null || !_curAction.MoveNext())
				{
					// If action is switched on the last iteration we end up
					// here, with a new action, which would be overwritten
					// with a default right away without this check.
					if (_curAction == prevAction)
					{
						switch (_state)
						{
							default:
							case AiState.Idle: this.SwitchAction(Idle); break;
							case AiState.Alert: this.SwitchAction(Alert); break;
							case AiState.Aggro: this.SwitchAction(Aggro); break;
							case AiState.Love: this.SwitchAction(Love); break;
						}

						_curAction.MoveNext();
					}
				}
			}
			catch (Exception ex)
			{
				Log.Exception(ex, "Exception in {0}", this.GetType().Name);
			}
			finally
			{
				_inside = false;
			}
		}

		/// <summary>
		/// Updates timestamp and returns DateTime.Now.
		/// </summary>
		/// <returns></returns>
		private DateTime UpdateTimestamp()
		{
			var now = DateTime.Now;
			_timestamp += (now - _lastBeat).TotalMilliseconds;
			return (_lastBeat = now);
		}

		/// <summary>
		/// Clears action, target, and sets state to Idle.
		/// </summary>
		private void Reset()
		{
			_curAction = null;
			_state = AiState.Idle;

			if (this.Creature.IsInBattleStance)
				this.Creature.IsInBattleStance = false;

			if (this.Creature.Target != null)
			{
				this.Creature.Target = null;
				Send.SetCombatTarget(this.Creature, 0, 0);
			}
		}

		/// <summary>
		/// Changes state based on (potential) targets.
		/// </summary>
		private void SelectState()
		{
			var pos = this.Creature.GetPosition();

			// Get perceivable targets
			var radius = Math.Max(_aggroRadius, _visualRadius);
			var potentialTargets = this.Creature.Region.GetVisibleCreaturesInRange(this.Creature, radius).Where(c => !c.Warping);
			potentialTargets = potentialTargets.Where(a => this.CanPerceive(pos, this.Creature.Direction, a.GetPosition()));

			// Stay in idle if there's no visible creature in aggro range
			if (!potentialTargets.Any() && this.Creature.Target == null)
			{
				if (_state != AiState.Idle)
					this.Reset();

				return;
			}

			// Find a new target
			if (this.Creature.Target == null)
			{
				// Get hated targets
				var hated = potentialTargets.Where(cr => !cr.IsDead && this.DoesHate(cr) && !cr.Has(CreatureStates.NamedNpc));
				var hatedCount = hated.Count();

				// Get doubted targets
				var doubted = potentialTargets.Where(cr => !cr.IsDead && this.DoesDoubt(cr) && !cr.Has(CreatureStates.NamedNpc));
				var doubtedCount = doubted.Count();

				// Get loved targets
				var loved = potentialTargets.Where(cr => !cr.IsDead && this.DoesLove(cr));
				var lovedCount = loved.Count();

				// Handle hate and doubt
				if (hatedCount != 0 || doubtedCount != 0)
				{
					// Try to hate first, then doubt
					if (hatedCount != 0)
						this.Creature.Target = hated.ElementAt(this.Random(hatedCount));
					else
						this.Creature.Target = doubted.ElementAt(this.Random(doubtedCount));

					// Switch to aware
					_state = AiState.Aware;
					_awareTime = DateTime.Now;
				}
				// Handle love
				else if (lovedCount != 0)
				{
					this.Creature.Target = loved.ElementAt(this.Random(lovedCount));

					_state = AiState.Love;
				}
				// Stop if no targets were found
				else return;

				// Stop for this tick, the aware delay needs a moment anyway
				return;
			}

			// TODO: Monsters switch targets under certain circumstances,
			//   e.g. a wolf will aggro a player, even if it has already
			//   noticed a cow.

			// Reset on...
			if (this.Creature.Target.IsDead																 // target dead
			|| !this.Creature.GetPosition().InRange(this.Creature.Target.GetPosition(), _aggroMaxRadius) // out of aggro range
			|| this.Creature.Target.Warping																 // target is warping
			|| this.Creature.Target.Client.State == ClientState.Dead									 // target disconnected
			|| (_state != AiState.Aggro && this.Creature.Target.Conditions.Has(ConditionsA.Invisible))	 // target hid before reaching aggro state
			)
			{
				this.Reset();
				return;
			}

			// Switch to alert from aware after the delay
			if (_state == AiState.Aware && DateTime.Now >= _awareTime + _alertDelay)
			{
				// Check if target is still in immediate range
				if (this.CanPerceive(pos, this.Creature.Direction, this.Creature.Target.GetPosition()))
				{
					_curAction = null;
					_state = AiState.Alert;
					_alertTime = DateTime.Now;
					this.Creature.IsInBattleStance = true;

					Send.SetCombatTarget(this.Creature, this.Creature.Target.EntityId, TargetMode.Alert);
				}
				// Reset if target ran away like a coward.
				else
				{
					this.Reset();
					return;
				}
			}

			// Switch to aggro from alert
			if (_state == AiState.Alert &&
			(
				// Aggro hated creatures after aggro delay
				(this.DoesHate(this.Creature.Target) && DateTime.Now >= _alertTime + _aggroDelay) ||

				// Aggro battle stance targets
				(_hatesBattleStance && this.Creature.Target.IsInBattleStance && DateTime.Now >= _alertTime + _hateBattleStanceDelay) ||

				// Hate over time
				(DateTime.Now >= _awareTime + _hateOverTimeDelay)
			))
			{
				// Check aggro limit
				var aggroCount = this.Creature.Region.CountAggro(this.Creature.Target, this.Creature.RaceId);
				if (aggroCount >= (int)_aggroLimit) return;

				_curAction = null;
				_state = AiState.Aggro;
				Send.SetCombatTarget(this.Creature, this.Creature.Target.EntityId, TargetMode.Aggro);
			}
		}

		/// <summary>
		/// Returns true if AI can hear or see at target pos from pos.
		/// </summary>
		/// <param name="pos">Position AI's creature is at.</param>
		/// <param name="direction">AI creature's current direction.</param>
		/// <param name="targetPos">Position of the potential target.</param>
		/// <returns></returns>
		protected virtual bool CanPerceive(Position pos, byte direction, Position targetPos)
		{
			return (this.CanHear(pos, targetPos) || this.CanSee(pos, direction, targetPos));
		}

		/// <summary>
		/// Returns true if target position is within hearing range.
		/// </summary>
		/// <param name="pos">Position from which AI creature listens.</param>
		/// <param name="targetPos">Position of the potential target.</param>
		/// <returns></returns>
		protected virtual bool CanHear(Position pos, Position targetPos)
		{
			return pos.InRange(targetPos, _aggroRadius);
		}

		/// <summary>
		/// Returns true if target position is within visual field.
		/// </summary>
		/// <param name="pos">Position from which AI creature listens.</param>
		/// <param name="direction">AI creature's current direction.</param>
		/// <param name="targetPos">Position of the potential target.</param>
		/// <returns></returns>
		protected virtual bool CanSee(Position pos, byte direction, Position targetPos)
		{
			return targetPos.InCone(pos, MabiMath.ByteToRadian(direction), _visualRadius, _visualRadian);
		}

		/// <summary>
		/// Idle state
		/// </summary>
		protected virtual IEnumerable Idle()
		{
			yield break;
		}

		/// <summary>
		/// Alert state
		/// </summary>
		protected virtual IEnumerable Alert()
		{
			yield break;
		}

		/// <summary>
		/// Aggro state
		/// </summary>
		protected virtual IEnumerable Aggro()
		{
			yield break;
		}

		/// <summary>
		/// Love state
		/// </summary>
		protected virtual IEnumerable Love()
		{
			yield break;
		}

		// Setup
		// ------------------------------------------------------------------

		/// <summary>
		/// Changes the hearbeat interval.
		/// </summary>
		/// <param name="interval"></param>
		protected void SetHeartbeat(int interval)
		{
			_heartbeat = Math.Max(MinHeartbeat, interval);
			_heartbeatTimer.Change(_heartbeat, _heartbeat);
		}

		/// <summary>
		/// Sets milliseconds before creature notices.
		/// </summary>
		/// <param name="time"></param>
		protected void SetAlertDelay(int time)
		{
			_alertDelay = TimeSpan.FromMilliseconds(time);
		}

		/// <summary>
		/// Sets milliseconds before creature attacks.
		/// </summary>
		/// <param name="time"></param>
		protected void SetAggroDelay(int time)
		{
			_aggroDelay = TimeSpan.FromMilliseconds(time);
		}

		/// <summary>
		/// Sets radius in which creatures become potential targets.
		/// </summary>
		/// <param name="radius"></param>
		protected void SetAggroRadius(int radius)
		{
			_aggroRadius = radius;
		}

		/// <summary>
		/// Sets visual field used for aggroing.
		/// </summary>
		/// <param name="radius"></param>
		/// <param name="angle"></param>
		protected void SetVisualField(int radius, double angle)
		{
			var a = Math2.Clamp(0, 160, (int)angle);

			_visualRadius = radius;
			_visualRadian = MabiMath.DegreeToRadian(a);
		}

		/// <summary>
		/// Milliseconds before creature attacks.
		/// </summary>
		/// <param name="limit"></param>
		protected void SetAggroLimit(AggroLimit limit)
		{
			_aggroLimit = limit;
		}

		/// <summary>
		/// Adds a race tag that the AI hates and will target.
		/// </summary>
		/// <param name="tags"></param>
		protected void Hates(params string[] tags)
		{
			foreach (var tag in tags)
			{
				var key = tag.Trim(' ', '/');
				if (_hateTags.ContainsKey(key))
					return;

				_hateTags.Add(key, tag);
			}
		}

		/// <summary>
		/// Adds a race tag that the AI likes and will not target unless
		/// provoked.
		/// </summary>
		/// <param name="tags"></param>
		protected void Loves(params string[] tags)
		{
			foreach (var tag in tags)
			{
				var key = tag.Trim(' ', '/');
				if (_loveTags.ContainsKey(key))
					return;

				_loveTags.Add(key, tag);
			}
		}

		/// <summary>
		/// Adds a race tag that the AI doubts.
		/// </summary>
		/// <param name="tags"></param>
		protected void Doubts(params string[] tags)
		{
			foreach (var tag in tags)
			{
				var key = tag.Trim(' ', '/');
				if (_hateTags.ContainsKey(key))
					return;

				_doubtTags.Add(key, tag);
			}
		}

		/// <summary>
		/// Specifies that the AI will go from alert into aggro when enemy
		/// changes into battle mode.
		/// </summary>
		protected void HatesBattleStance(int delay = 3000)
		{
			_hatesBattleStance = true;
			_hateBattleStanceDelay = TimeSpan.FromMilliseconds(delay);
		}

		/// <summary>
		/// Specifies that the AI will go from alert into aggro when a
		/// doubted target sticks around for too long.
		/// </summary>
		/// <param name="delay"></param>
		protected void HatesNearby(int delay = 6000)
		{
			_hateOverTimeDelay = TimeSpan.FromMilliseconds(delay);
		}

		/// <summary>
		/// Sets the max distance an NPC can wander away from its spawn.
		/// </summary>
		/// <param name="distance"></param>
		protected void SetMaxDistanceFromSpawn(int distance)
		{
			_maxDistanceFromSpawn = distance;
		}

		/// <summary>
		/// Registers a reaction.
		/// </summary>
		/// <param name="ev">The event on which func should be executed.</param>
		/// <param name="func">The reaction to the event.</param>
		protected void On(AiState state, AiEventType ev, Func<IEnumerable> func)
		{
			this.On(state, ev, SkillId.None, func);
		}

		/// <summary>
		/// Registers a reaction.
		/// </summary>
		/// <param name="state">The state the event is for.</param>
		/// <param name="ev">The event on which func should be executed.</param>
		/// <param name="skillId">The skill the should trigger the event.</param>
		/// <param name="func">The reaction to the event.</param>
		protected void On(AiState state, AiEventType ev, SkillId skillId, Func<IEnumerable> func)
		{
			lock (_reactions)
			{
				if (!_reactions[state].ContainsKey(ev))
					_reactions[state][ev] = new Dictionary<SkillId, Func<IEnumerable>>();
				_reactions[state][ev][skillId] = func;
			}
		}

		// Functions
		// ------------------------------------------------------------------

		/// <summary>
		/// Returns random number between 0.0 and 100.0.
		/// </summary>
		/// <returns></returns>
		protected double Random()
		{
			lock (_rnd)
				return (100 * _rnd.NextDouble());
		}

		/// <summary>
		/// Returns random number between 0 and max-1.
		/// </summary>
		/// <param name="max">Exclusive upper bound</param>
		/// <returns></returns>
		protected int Random(int max)
		{
			return this.Random(0, max);
		}

		/// <summary>
		/// Returns random number between min and max-1.
		/// </summary>
		/// <param name="min">Inclusive lower bound</param>
		/// <param name="max">Exclusive upper bound</param>
		/// <returns></returns>
		protected int Random(int min, int max)
		{
			lock (_rnd)
				return _rnd.Next(min, max);
		}

		/// <summary>
		/// Returns a random value from the given ones.
		/// </summary>
		/// <param name="values"></param>
		protected T Rnd<T>(params T[] values)
		{
			lock (_rnd)
				return _rnd.Rnd(values);
		}

		/// <summary>
		/// Returns true if AI hates target creature.
		/// </summary>
		/// <param name="target"></param>
		/// <returns></returns>
		protected bool DoesHate(Creature target)
		{
			return _hateTags.Values.Any(tag => target.RaceData.HasTag(tag));
		}

		/// <summary>
		/// Returns true if AI loves target creature.
		/// </summary>
		/// <param name="target"></param>
		/// <returns></returns>
		protected bool DoesLove(Creature target)
		{
			return _loveTags.Values.Any(tag => target.RaceData.HasTag(tag));
		}

		/// <summary>
		/// Returns true if AI doubts target creature.
		/// </summary>
		/// <param name="target"></param>
		/// <returns></returns>
		protected bool DoesDoubt(Creature target)
		{
			return _doubtTags.Values.Any(tag => target.RaceData.HasTag(tag));
		}

		/// <summary>
		/// Sends SharpMind to all applicable creatures.
		/// </summary>
		/// <remarks>
		/// The Wiki is speaking of a passive Sharp Mind skill, but it doesn't
		/// seem to be a skill at all anymore.
		/// 
		/// A failed Sharp Mind is supposed to be displayed as an "X",
		/// assumingly statuses 3 and 4 were used for this in the past,
		/// but the current NA client doesn't do anything when sending
		/// them, so we use skill id 0 instead, which results in a
		/// question mark, originally used for skills unknown to the
		/// player.
		/// 
		/// Even on servers that didn't have Sharp Mind officially,
		/// the packets were still sent to the client, it just didn't
		/// display them, assumingly because the players didn't have
		/// the skill. Since this is not the case for the NA client,
		/// we control it from the server.
		/// 
		/// TODO: When we move AIs to an NPC client, the entire SharpMind
		///   handling would move to the SkillPrepare handler.
		/// </remarks>
		/// <param name="skillId"></param>
		/// <param name="status"></param>
		protected void SharpMind(SkillId skillId, SharpMindStatus status)
		{
			// Some races are "immune" to Sharp Mind
			if (this.Creature.RaceData.SharpMindImmune)
				return;

			// Check if SharpMind is enabled
			if (!AuraData.FeaturesDb.IsEnabled("SharpMind"))
				return;

			var passive = AuraData.FeaturesDb.IsEnabled("PassiveSharpMind");

			// Send to players in range, one after the other, so we have control
			// over the recipients.
			foreach (var creature in _playersInRange)
			{
				// Handle active (old) Sharp Mind
				if (!passive)
				{
					// Don't send if player doesn't have Sharp Mind.
					if (!creature.Skills.Has(SkillId.SharpMind))
						continue;

					// Set skill id to 0, so the bubble displays a question mark,
					// if skill is unknown to the player or Sharp Mind fails.
					if (!creature.Skills.Has(skillId) || this.Random() >= ChannelServer.Instance.Conf.World.SharpMindChance)
						skillId = SkillId.None;
				}

				// Cancel and None are sent for removing the bubble
				if (status == SharpMindStatus.Cancelling || status == SharpMindStatus.None)
				{
					Send.SharpMind(this.Creature, creature, skillId, SharpMindStatus.Cancelling);
					Send.SharpMind(this.Creature, creature, skillId, SharpMindStatus.None);
				}
				else
				{
					Send.SharpMind(this.Creature, creature, skillId, status);
				}
			}
		}

		/// <summary>
		/// Proxy for Localization.Get.
		/// </summary>
		/// <param name="phrase"></param>
		protected static string L(string phrase)
		{
			return Localization.Get(phrase);
		}

		/// <summary>
		/// Proxy for Localization.GetParticular.
		/// </summary>
		/// <param name="context"></param>
		/// <param name="phrase"></param>
		protected static string LX(string context, string phrase)
		{
			return Localization.GetParticular(context, phrase);
		}

		/// <summary>
		/// Proxy for Localization.GetPlural.
		/// </summary>
		/// <param name="phrase"></param>
		/// <param name="phrasePlural"></param>
		/// <param name="count"></param>
		protected static string LN(string phrase, string phrasePlural, int count)
		{
			return Localization.GetPlural(phrase, phrasePlural, count);
		}

		/// <summary>
		/// Proxy for Localization.GetParticularPlural.
		/// </summary>
		/// <param name="context"></param>
		/// <param name="phrase"></param>
		/// <param name="phrasePlural"></param>
		/// <param name="count"></param>
		protected static string LXN(string context, string phrase, string phrasePlural, int count)
		{
			return Localization.GetParticularPlural(context, phrase, phrasePlural, count);
		}

		/// <summary>
		/// Returns true if AI creature has the skill.
		/// </summary>
		/// <param name="skillId"></param>
		/// <returns></returns>
		protected bool HasSkill(SkillId skillId)
		{
			return this.Creature.Skills.Has(skillId);
		}

		/// <summary>
		/// Generates and saves a random number between 0 and 99
		/// for Case to use.
		/// </summary>
		/// <remarks>
		/// SwitchRandom only keeps track of one random number at a time.
		/// You can nest SwitchRandom-if-constructs, but randomly calling
		/// SwitchRandom in between might give unexpected results.
		/// </remarks>
		/// <example>
		/// SwitchRandom();
		/// if (Case(40))
		/// {
		///     Do(Wander(250, 500));
		/// }
		/// else if (Case(40))
		/// {
		///     Do(Wander(250, 500, false));
		/// }
		/// else if (Case(20))
		/// {
		///     Do(Wait(4000, 6000));
		/// }
		/// 
		/// SwitchRandom();
		/// if (Case(60))
		/// {
		///		SwitchRandom();
		///		if (Case(20))
		///		{
		///		    Do(Wander(250, 500));
		///		}
		///		else if (Case(80))
		///		{
		///		    Do(Wait(4000, 6000));
		///		}
		/// }
		/// else if (Case(40))
		/// {
		///     Do(Wander(250, 500, false));
		/// }
		/// </example>
		protected void SwitchRandom()
		{
			_switchRandomN = this.Random(100);
			_switchRandomM = 0;
		}

		/// <summary>
		/// Returns true if value matches the last random percentage
		/// generated by SwitchRandom().
		/// </summary>
		/// <param name="value"></param>
		/// <returns></returns>
		protected bool Case(int value)
		{
			_switchRandomM += value;
			return (_switchRandomN < _switchRandomM);
		}

		// Flow control
		// ------------------------------------------------------------------

		/// <summary>
		/// Clears AI and sets new current action.
		/// </summary>
		/// <param name="action"></param>
		protected void SwitchAction(Func<IEnumerable> action)
		{
			this.ExecuteOnce(this.CancelSkill());

			// Cancel rest
			if (this.Creature.Has(CreatureStates.SitDown))
			{
				var restHandler = ChannelServer.Instance.SkillManager.GetHandler<Rest>(SkillId.Rest);
				if (restHandler != null)
					restHandler.Stop(this.Creature, this.Creature.Skills.Get(SkillId.Rest));
			}

			_curAction = action().GetEnumerator();
		}

		/// <summary>
		/// Creates enumerator and runs it once.
		/// </summary>
		/// <remarks>
		/// Useful if you want to make a creature go somewhere, but you don't
		/// want to wait for it to arrive there. Effectively running the action
		/// with a 0 timeout.
		/// </remarks>
		/// <param name="action"></param>
		protected void ExecuteOnce(IEnumerable action)
		{
			action.GetEnumerator().MoveNext();
		}

		/// <summary>
		/// Sets target and puts creature into battle mode.
		/// </summary>
		/// <param name="target"></param>
		public void AggroCreature(Creature target)
		{
			_curAction = null;
			_state = AiState.Aggro;
			this.Creature.IsInBattleStance = true;
			this.Creature.Target = target;
			Send.SetCombatTarget(this.Creature, this.Creature.Target.EntityId, TargetMode.Aggro);
		}
	}
}

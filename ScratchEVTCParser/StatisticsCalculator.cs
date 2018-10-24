using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.UI.WebControls;
using ScratchEVTCParser.Events;
using ScratchEVTCParser.GameData;
using ScratchEVTCParser.Model;
using ScratchEVTCParser.Model.Agents;
using ScratchEVTCParser.Model.Skills;
using ScratchEVTCParser.Statistics;
using ScratchEVTCParser.Statistics.Buffs;
using ScratchEVTCParser.Statistics.PlayerDataParts;
using ScratchEVTCParser.Statistics.RotationItems;
using SkillSlot = ScratchEVTCParser.Model.Skills.SkillSlot;

namespace ScratchEVTCParser
{
	public class StatisticsCalculator
	{
		public BuffSimulator BuffSimulator { get; set; } = new BuffSimulator();
		public GameSkillDataRepository GameSkillDataRepository { get; set; } = new GameSkillDataRepository();
		public SpecializationDetections SpecializationDetections { get; set; } = new SpecializationDetections();
		public SkillDetections SkillDetections { get; set; } = new SkillDetections();

		/// <summary>
		/// Calculates statistics for an encounter, such as damage done...
		/// </summary>
		/// <param name="log">The processed log.</param>
		/// <param name="data">Data from the GW2 API, may be null, some statistics won't be calculated.</param>
		/// <returns></returns>
		public LogStatistics GetStatistics(Log log, GW2ApiData apiData)
		{
			var phaseStats = new List<PhaseStats>();
			foreach (var phase in log.Encounter.GetPhases())
			{
				var targetDamageData = new List<TargetSquadDamageData>();
				long phaseDuration = phase.EndTime - phase.StartTime;
				foreach (var target in phase.ImportantEnemies)
				{
					var damageData = GetDamageData(
						log.Agents,
						phase.Events
							.OfType<DamageEvent>()
							.Where(x => x.Defender == target)
							.ToArray(),
						phaseDuration);
					targetDamageData.Add(new TargetSquadDamageData(target, phaseDuration, damageData.Values));
				}

				var totalDamageData = new SquadDamageData(phaseDuration,
					GetDamageData(
						log.Agents,
						phase.Events.OfType<DamageEvent>().ToArray(),
						phaseDuration).Values
				);

				phaseStats.Add(new PhaseStats(phase.Name, phase.StartTime, phase.EndTime, targetDamageData,
					totalDamageData));
			}

			var fightTime = log.Encounter.GetPhases().Sum(x => x.EndTime - x.StartTime);
			var fullFightSquadDamageData = new SquadDamageData(fightTime,
				GetDamageData(log.Agents, log.Events.OfType<DamageEvent>().ToArray(), fightTime).Values);

			var fullFightTargetDamageData = new List<TargetSquadDamageData>();
			foreach (var target in log.Encounter.ImportantEnemies)
			{
				var damageData = GetDamageData(
					log.Agents,
					log.Events
						.OfType<DamageEvent>()
						.Where(x => x.Defender == target)
						.ToArray(),
					fightTime);
				fullFightTargetDamageData.Add(new TargetSquadDamageData(target, fightTime, damageData.Values));
			}

			var eventCounts = new Dictionary<Type, int>();
			foreach (var e in log.Events)
			{
				var type = e.GetType();
				if (!eventCounts.ContainsKey(type))
				{
					eventCounts[type] = 0;
				}

				eventCounts[type]++;
			}

			var eventCountsByName =
				eventCounts.Select(x => (x.Key.Name, x.Value)).ToDictionary(x => x.Item1, x => x.Item2);

			var logAuthor = log.Events.OfType<PointOfViewEvent>().First().RecordingAgent as Player;
			var startTime = log.Events.OfType<LogStartEvent>().First().ServerTime;

			var might = log.Skills.FirstOrDefault(x => x.Id == SkillIds.Might);
			var vulnerability = log.Skills.FirstOrDefault(x => x.Id == SkillIds.Vulnerability);
			if (might != null) BuffSimulator.TrackBuff(might, BuffSimulationType.Intensity, 25);
			if (vulnerability != null) BuffSimulator.TrackBuff(vulnerability, BuffSimulationType.Intensity, 25);

			var buffData = BuffSimulator.SimulateBuffs(log.Agents, log.Events.OfType<BuffEvent>(),
				log.Encounter.GetPhases().Last().EndTime);

			var playerData = GetPlayerData(log, apiData);

			return new LogStatistics(startTime, logAuthor, playerData, phaseStats, fullFightSquadDamageData,
				fullFightTargetDamageData, buffData, log.Encounter.GetResult(), log.Encounter.GetName(),
				log.EVTCVersion, eventCountsByName, log.Agents, log.Skills);
		}

		private IEnumerable<PlayerData> GetPlayerData(Log log, GW2ApiData apiData)
		{
			var players = log.Agents.OfType<Player>().ToArray();

			var deathCounts = players.ToDictionary(x => x, x => 0);
			var downCounts = players.ToDictionary(x => x, x => 0);
			var usedSkills = players.ToDictionary(x => x, x => new HashSet<Skill>());

			foreach (var deadEvent in log.Events.OfType<AgentDeadEvent>().Where(x => x.Agent is Player))
			{
				var player = (Player) deadEvent.Agent;
				deathCounts[player]++;
			}

			foreach (var downEvent in log.Events.OfType<AgentDownedEvent>().Where(x => x.Agent is Player))
			{
				var player = (Player) downEvent.Agent;
				downCounts[player]++;
			}

			// Buff damage events only tell us which conditions/buffs, not what skill actually applied them
			foreach (var damageEvent in log.Events.OfType<PhysicalDamageEvent>().Where(x => x.Attacker is Player))
			{
				var player = (Player) damageEvent.Attacker;
				usedSkills[player].Add(damageEvent.Skill);
			}

			foreach (var activationEvent in log.Events.OfType<SkillCastEvent>().Where(x => x.Agent is Player))
			{
				var player = (Player) activationEvent.Agent;
				usedSkills[player].Add(activationEvent.Skill);
			}

			var playerData = new List<PlayerData>();
			foreach (var player in players)
			{
				HashSet<SkillData> utilitySkills = null;
				HashSet<SkillData> healingSkills = null;
				HashSet<SkillData> eliteSkills = null;
				if (apiData != null)
				{
					utilitySkills = new HashSet<SkillData>();
					healingSkills = new HashSet<SkillData>();
					eliteSkills = new HashSet<SkillData>();
					foreach (var usedSkill in usedSkills[player])
					{
						var skillData = apiData.GetSkillData(usedSkill);

						// Skills may be also registered as used if they affect other players and do damage through them
						if (skillData != null && skillData.Professions.Contains(player.Profession))
						{
							if (skillData.Slot == SkillSlot.Elite)
							{
								eliteSkills.Add(skillData);
							}
							else if (skillData.Slot == SkillSlot.Utility)
							{
								utilitySkills.Add(skillData);
							}
							else if (skillData.Slot == SkillSlot.Heal)
							{
								healingSkills.Add(skillData);
							}
						}
					}
				}

				WeaponType land1Weapon1 = WeaponType.Other;
				WeaponType land1Weapon2 = WeaponType.Other;
				WeaponType land2Weapon1 = WeaponType.Other;
				WeaponType land2Weapon2 = WeaponType.Other;
				IEnumerable<SkillData> land1WeaponSkills = null;
				IEnumerable<SkillData> land2WeaponSkills = null;

				// TODO: Dual wield skill handling for Thieves
				if (apiData != null)
				{
					WeaponSet currentWeaponSet = WeaponSet.Unknown;
					// We are only interested in land weapons. This may be imperfect if started on an underwater set.
					var firstWeaponSwap = log.Events.OfType<AgentWeaponSwapEvent>().FirstOrDefault(x =>
						x.NewWeaponSet == WeaponSet.Land1 || x.NewWeaponSet == WeaponSet.Land2);

					if (firstWeaponSwap == null)
					{
						currentWeaponSet = WeaponSet.Land1;
					}
					else
					{
						// First weapon set is the other one than the first swap swaps to (unless it was an underwater one)
						currentWeaponSet = firstWeaponSwap.NewWeaponSet == WeaponSet.Land1
							? WeaponSet.Land2
							: WeaponSet.Land1;
					}

					foreach (var logEvent in log.Events)
					{
						if (logEvent is AgentWeaponSwapEvent weaponSwapEvent && weaponSwapEvent.Agent == player)
						{
							currentWeaponSet = weaponSwapEvent.NewWeaponSet;
							continue;
						}

						SkillData skillData = null;
						if (logEvent is StartSkillCastEvent castEvent && castEvent.Agent == player)
						{
							skillData = apiData.GetSkillData(castEvent.Skill);
						}

						if (skillData != null)
						{
							if (skillData.Professions.Contains(player.Profession) && skillData.Type == SkillType.Weapon)
							{
								if (skillData.WeaponType.IsTwoHanded() || skillData.Slot == SkillSlot.Weapon1 ||
								    skillData.Slot == SkillSlot.Weapon2 || skillData.Slot == SkillSlot.Weapon3)
								{
									if (currentWeaponSet == WeaponSet.Land1)
									{
										land1Weapon1 = skillData.WeaponType;
									}
									else if (currentWeaponSet == WeaponSet.Land2)
									{
										land2Weapon1 = skillData.WeaponType;
									}
								}

								if (skillData.WeaponType.IsTwoHanded() || skillData.Slot == SkillSlot.Weapon4 ||
								    skillData.Slot == SkillSlot.Weapon5)
								{
									if (currentWeaponSet == WeaponSet.Land1)
									{
										land1Weapon2 = skillData.WeaponType;
									}
									else if (currentWeaponSet == WeaponSet.Land2)
									{
										land2Weapon2 = skillData.WeaponType;
									}
								}
							}
						}
					}

					land1WeaponSkills = GameSkillDataRepository
						.GetWeaponSkillIds(player.Profession, land1Weapon1, land1Weapon2)
						.Select(x => x == -1 ? null : apiData.GetSkillData(x));
					land2WeaponSkills = GameSkillDataRepository
						.GetWeaponSkillIds(player.Profession, land2Weapon1, land2Weapon2)
						.Select(x => x == -1 ? null : apiData.GetSkillData(x));
				}

				if (apiData != null)
				{
					var skillDetections = SkillDetections.GetSkillDetections(player.Profession).ToArray();
					foreach (var e in log.Events)
					{
						foreach (var detection in skillDetections)
						{
							if (detection.Detection.IsDetected(player, e))
							{
								var skill = apiData.GetSkillData(detection.SkillId);
								if (detection.Slot == SkillSlot.Utility)
								{
									utilitySkills.Add(skill);
								}
								else if (detection.Slot == SkillSlot.Heal)
								{
									healingSkills.Add(skill);
								}
								else if (detection.Slot == SkillSlot.Elite)
								{
									eliteSkills.Add(skill);
								}
							}
						}
					}
				}

				var ignoredSkills = SkillDetections.GetIgnoredSkillIds(player.Profession);
				healingSkills?.RemoveWhere(x => ignoredSkills.Contains(x.Id));
				utilitySkills?.RemoveWhere(x => ignoredSkills.Contains(x.Id));
				eliteSkills?.RemoveWhere(x => ignoredSkills.Contains(x.Id));

				var specializationDetections =
					SpecializationDetections.GetSpecializationDetections(player.Profession).ToArray();
				var badges = new List<PlayerBadge>();

				var specializations = new HashSet<CoreSpecialization>();
				foreach (var e in log.Events)
				{
					foreach (var detection in specializationDetections)
					{
						if (detection.Detection.IsDetected(player, e))
						{
							specializations.Add(detection.Specialization);
						}
					}
				}

				foreach (var spec in specializations.OrderBy(x => x.ToString()))
				{
					badges.Add(new PlayerBadge(spec.ToString(), BadgeType.Specialization));
				}

				var rotation = GetRotation(log, player, apiData);

				var data = new PlayerData(player, downCounts[player], deathCounts[player], rotation, usedSkills[player],
					healingSkills, utilitySkills, eliteSkills, land1Weapon1, land1Weapon2, land2Weapon1, land2Weapon2,
					land1WeaponSkills, land2WeaponSkills, badges);

				playerData.Add(data);
			}

			return playerData;
		}

		private PlayerRotation GetRotation(Log log, Player player, GW2ApiData apiData)
		{
			long startTime = log.Events.OfType<LogStartEvent>().FirstOrDefault()?.Time ?? 0;

			var rotation = new List<RotationItem>();

			long castStart = startTime;
			foreach (var logEvent in log.Events.OfType<AgentEvent>().Where(x => x.Agent == player))
			{
				long time = logEvent.Time - startTime;
				if (logEvent is StartSkillCastEvent startSkillCastEvent)
				{
					castStart = logEvent.Time;
				}
				else if (logEvent is ResetSkillCastEvent resetSkillCastEvent)
				{
					var skill = resetSkillCastEvent.Skill;
					var skillData = apiData?.GetSkillData(skill);
					rotation.Add(new SkillCastItem(castStart, time, SkillCastType.Reset, skill, skillData));
					castStart = logEvent.Time;
				}
				else if (logEvent is EndSkillCastEvent cancelledSkillCastEvent)
				{
					var skill = cancelledSkillCastEvent.Skill;
					var skillData = apiData?.GetSkillData(skill);
					var type = cancelledSkillCastEvent.EndType == EndSkillCastEvent.SkillEndType.Cancel
						? SkillCastType.Cancel
						: SkillCastType.Success;
					rotation.Add(new SkillCastItem(castStart, time, type, skill, skillData));
				}
				else if (logEvent is AgentWeaponSwapEvent weaponSwapEvent)
				{
					rotation.Add(new WeaponSwapItem(time, weaponSwapEvent.NewWeaponSet));
				}

				// TODO: Add downed
				// TODO: Add death
				// TODO: Add reviving
			}

			return new PlayerRotation(player, rotation);
		}

		private Dictionary<Agent, DamageData> GetDamageData(IEnumerable<Agent> agents, ICollection<DamageEvent> events,
			long phaseDuration)
		{
			var physicalBossDamages = events.OfType<PhysicalDamageEvent>();
			var conditionBossDamages = events.OfType<BuffDamageEvent>();

			var damageDataByAttacker = new Dictionary<Agent, DamageData>();

			// Ensure all players are always in the damage data, even if they did no damage.
			foreach (var player in agents.OfType<Player>())
			{
				if (!damageDataByAttacker.ContainsKey(player))
				{
					damageDataByAttacker[player] = new DamageData(player, phaseDuration, 0, 0);
				}
			}

			foreach (var damageEvent in physicalBossDamages)
			{
				var attacker = damageEvent.Attacker;

				long damage = damageEvent.Damage;
				if (attacker == null)
				{
					continue; // TODO: Save as unknown damage
				}

				var mainMaster = attacker;
				while (mainMaster.Master != null)
				{
					mainMaster = attacker.Master;
				}

				if (!damageDataByAttacker.ContainsKey(mainMaster))
				{
					damageDataByAttacker[mainMaster] = new DamageData(mainMaster, phaseDuration, 0, 0);
				}

				damageDataByAttacker[mainMaster] += new DamageData(mainMaster, phaseDuration, damage, 0);
			}

			foreach (var damageEvent in conditionBossDamages)
			{
				var attacker = damageEvent.Attacker;

				long damage = damageEvent.Damage;
				if (attacker == null)
				{
					continue; // TODO: Save as unknown damage
				}

				var mainMaster = attacker;
				while (mainMaster.Master != null)
				{
					mainMaster = attacker.Master;
				}

				if (!damageDataByAttacker.ContainsKey(mainMaster))
				{
					damageDataByAttacker[mainMaster] = new DamageData(mainMaster, phaseDuration, 0, 0);
				}

				damageDataByAttacker[mainMaster] += new DamageData(mainMaster, phaseDuration, 0, damage);
			}

			return damageDataByAttacker;
		}
	}
}
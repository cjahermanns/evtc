using System.Collections.Generic;
using GW2Scratch.EVTCAnalytics.Model.Agents;
using GW2Scratch.EVTCAnalytics.Model.Skills;

namespace GW2Scratch.EVTCAnalytics.Statistics.Buffs
{
	public interface IBuffStackCollection
	{
		Skill Buff { get; }
		IEnumerable<BuffStatusSegment> BuffSegments { get; }

		void AddStack(long timeFrom, long timeTo, Agent source);

		void RemoveAllStacks(long time);

		void RemoveStack(long time, long durationRemaining);

		void FinalizeCollection(long time);

		int GetStackCount(long time);
	}
}
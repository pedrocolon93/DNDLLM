using UnityEngine;
using System.Linq;

namespace DNDLLM.Utils
{
    public static class DiceRoller
    {
        public static (int total, int[] rolls) Roll(int sides, int count = 1)
        {
            int[] rolls = new int[count];
            for (int i = 0; i < count; i++)
            {
                rolls[i] = Random.Range(1, sides + 1);
            }
            return (rolls.Sum(), rolls);
        }

        public static int RollCheck(int bonus = 0)
        {
            return Random.Range(1, 21) + bonus;
        }
    }
}

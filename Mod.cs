using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
#if !v1_2
using RimWorld.Planet;
#endif
using Verse;

namespace BigBedBuff
{
    public class BigBedBuff : Mod
    {
        public static ThoughtDef thought;
        public BigBedBuff(ModContentPack content) : base(content)
        {
            LongEventHandler.QueueLongEvent(delegate
            {
                thought = ThoughtDef.Named("SleptAloneInBigBed");
                PatchApplier.ApplyPatches();
            }, "BigBedBuff.Mod.ctor", false, null);
        }
    }
    public static class PatchApplier
    {
        private static readonly Harmony harmony 
            = new Harmony(id: "AmCh.BigBedBuff");
        private static readonly MethodInfo m_ApplyBedThoughts
            = typeof(Toils_LayDown).GetMethod("ApplyBedThoughts",
                BindingFlags.Static | BindingFlags.NonPublic);
        public static void ApplyPatches()
            => harmony.Patch(m_ApplyBedThoughts, 
                transpiler: Patch_ApplyBedThoughts.transpiler);
    }
    public static class Patch_ApplyBedThoughts
    {
        public static HarmonyMethod transpiler
            = new HarmonyMethod((
                (Func<IEnumerable<CodeInstruction>, 
                IEnumerable<CodeInstruction>>)
                Transpiler).Method);
        private static void RemoveThoughtFromPawn(Pawn pawn)
            => pawn.needs.mood.thoughts.memories
            .RemoveMemoriesOfDef(BigBedBuff.thought);
        private static void AddThoughtToPawn(Pawn pawn, Building_Bed bed)
        {
            if (bed == null ||
                bed.CostListAdjusted().Count == 0 ||
                bed != pawn.ownership.OwnedBed ||
                bed.GetComp<CompAssignableToPawn>()
                .AssignedPawnsForReading.Count > 1) 
                return;
#if !v1_2
            List<DirectPawnRelation> partners
                = LovePartnerRelationUtility
                .ExistingLovePartners(pawn, allowDead: false);
            if (partners.Any(x =>
                x.otherPawn.IsColonist 
                &&
                !x.otherPawn.IsWorldPawn() 
                &&
                x.otherPawn.relations.everSeenByPlayer 
                &&
                x.otherPawn.IsSlaveOfColony == pawn.IsSlaveOfColony 
                && 
                (
                    (
                        x.def == PawnRelationDefOf.Spouse 
                        &&
                        new HistoryEvent(HistoryEventDefOf.SharedBed_Spouse, 
                        pawn.Named(HistoryEventArgsNames.Doer)).DoerWillingToDo()
                    ) 
                    ||
                    (
                        x.def != PawnRelationDefOf.Spouse 
                        &&
                        new HistoryEvent(HistoryEventDefOf.SharedBed_NonSpouse, 
                        pawn.Named(HistoryEventArgsNames.Doer)).DoerWillingToDo()
                    )
                ))) return;
#endif
            pawn.needs.mood.thoughts.memories.TryGainMemory(BigBedBuff.thought);
        }
        private static readonly MethodInfo
            m_RemoveThoughtFromPawn 
                = ((Action<Pawn>)RemoveThoughtFromPawn).Method,
            m_AddThoughtToPawn 
                = ((Action<Pawn, Building_Bed>)AddThoughtToPawn).Method,
            m_RemoveMemoriesOfDef 
                = typeof(MemoryThoughtHandler)
                .GetMethod(nameof(MemoryThoughtHandler.RemoveMemoriesOfDef));
#if !v1_2
        private static readonly MethodInfo
            m_Notify_AddBedThoughts = typeof(Pawn).GetMethod(nameof(Pawn.Notify_AddBedThoughts));
#endif
        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            ReadOnlyCollection<CodeInstruction> instructionList 
                = instructions.ToList().AsReadOnly();
            int state = 0;
            for (int i = 0; i < instructionList.Count; i++)
            {
                CodeInstruction codeInstruction = instructionList[i];
                if (state == 0 && i > 0 &&
                    instructionList[i - 1].opcode == OpCodes.Ret)
                {
                    // Right after the first OpCodes.Ret Vanilla Begins to Remove all BedThoughts
                    //   We remove ours too so we can reapply them
                    state = 1;
                    yield return new CodeInstruction(OpCodes.Ldarg_0)
                        .WithLabels(codeInstruction.ExtractLabels());
                    yield return new CodeInstruction(OpCodes.Call, m_RemoveThoughtFromPawn);
                    yield return codeInstruction;
                    continue;
                }
#if v1_2
                if (state == 1 && i < instructionList.Count - 1 &&
                    instructionList[i + 1].opcode == OpCodes.Ret)
#else
                if (state == 1 && i < instructionList.Count - 1 &&
                    codeInstruction.IsLdarg(0) &&
                    instructionList[i + 1].Calls(m_Notify_AddBedThoughts))
#endif
                {
                    // Just before the call to m_AddThoughtToPawn
                    //   Vanilla finished adding all BedThoughts
                    // We check and add ours
                    state = 2;
                    yield return new CodeInstruction(OpCodes.Ldarg_0)
                        .WithLabels(codeInstruction.ExtractLabels());
                    yield return new CodeInstruction(OpCodes.Ldarg_1);
                    yield return new CodeInstruction(OpCodes.Call, m_AddThoughtToPawn);
                    yield return codeInstruction;
                    continue;
                }
                yield return codeInstruction;
            }
        }
    }
}

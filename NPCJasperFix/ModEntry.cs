using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Harmony;
using CIL = Harmony.CodeInstruction;
using StardewModdingAPI;
using StardewValley;
using System.Reflection.Emit;
using System.Reflection;

namespace NPCJasperFix
{
    /// <summary>The mod entry point.</summary>
    public class ModEntry : Mod
    {
        internal static ModEntry Instance { get; private set; }
        internal HarmonyInstance Harmony { get; private set; }
        public static IMonitor ModMonitor { get; private set; }

        /*********
        ** Public methods
        *********/
        /// <summary>The mod entry point, called after the mod is first loaded.</summary>
        /// <param name="helper">Provides simplified APIs for writing mods.</param>
        public override void Entry(IModHelper helper)
        {
            // Make resources available.
            Instance = this;
            ModMonitor = this.Monitor;
            Harmony = HarmonyInstance.Create(this.ModManifest.UniqueID);

            // Apply the patch to stop Jas from going silent
            Harmony.Patch(
                original: helper.Reflection.GetMethod(new NPC(), "loadCurrentDialogue").MethodInfo,
                transpiler: new HarmonyMethod(AccessTools.Method(typeof(NPCPatch), nameof(NPCPatch.loadCurrentDialogue_Transpiler)))
            );
        }

        /*********
        ** Harmony patches
        *********/
        /// <summary>Contains patches for patching game code in the StardewValley.NPC class.</summary>
        internal class NPCPatch
        {
            /// <summary>Changes the Game1.player.spouse.Contains check to use Game1.player.spouse.Equals instead.</summary>
            public static IEnumerable<CIL> loadCurrentDialogue_Transpiler(IEnumerable<CIL> instructions)
            {
                try
                {
                    var codes = new List<CodeInstruction>(instructions);

                    for (int i = 0; i < codes.Count - 5; i++)
                    {
                        // This is the snippet of code we want to find and change:
                        //     OLD Game1.player.spouse.Contains(this.Name)
                        //     NEW Game1.player.spouse.Equals(this.Name)
                        // It can be done with a single opcode call change from string.Contains to string.Equals

                        /*
                        call class StardewValley.Farmer StardewValley.Game1::get_player()
                        callvirt instance string StardewValley.Farmer::get_spouse()
                        ldarg.0
                        call instance string StardewValley.Character::get_Name()
                        callvirt instance bool[mscorlib] System.String::Contains(string)*/

                        if (//call class StardewValley.Farmer StardewValley.Game1::get_player()
                            codes[i].opcode == OpCodes.Call &&
                            (MethodInfo)codes[i].operand == typeof(Game1).GetProperty("player").GetGetMethod() &&
                            //callvirt instance string StardewValley.Farmer::get_spouse()
                            codes[i + 1].opcode == OpCodes.Callvirt &&
                            (MethodInfo)codes[i + 1].operand == typeof(Farmer).GetProperty("spouse").GetGetMethod() &&
                            //ldarg.0
                            codes[i + 2].opcode == OpCodes.Ldarg_0 &&
                            //call instance string StardewValley.Character::get_Name()
                            codes[i + 3].opcode == OpCodes.Call &&
                            (MethodInfo)codes[i + 3].operand == typeof(Character).GetProperty("Name").GetGetMethod() &&
                            //callvirt instance bool[mscorlib] System.String::Contains(string)
                            codes[i + 4].opcode == OpCodes.Callvirt &&
                            (MethodInfo)codes[i + 4].operand == typeof(string).GetMethod("Contains", new Type[] { typeof(string) }) )
                        {
                            // Insert the new replacement instruction
                            codes[i + 4] = new CIL(OpCodes.Callvirt, typeof(String).GetMethod("Equals", new Type[] { typeof(string) }) );
                            
                            ModMonitor.LogOnce($"Patched NPC.loadCurrentDialogue for Jasper NPC: {nameof(loadCurrentDialogue_Transpiler)}", LogLevel.Trace);
                        }
                    }
                    return codes.AsEnumerable();
                }
                catch (Exception ex)
                {
                    ModMonitor.Log($"Failed in {nameof(loadCurrentDialogue_Transpiler)}:\n{ex}", LogLevel.Error);
                    return instructions; // use original code
                }
            }
        }
    }
}
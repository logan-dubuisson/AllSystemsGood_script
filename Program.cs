using EmptyKeys.UserInterface.Generated;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Security.Permissions;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.Network;
using VRageMath;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        // This file contains your actual script.
        //
        // You can either keep all your code here, or you can create separate
        // code files to make your program easier to navigate while coding.
        //
        // Go to:
        // https://github.com/malware-dev/MDK-SE/wiki/Quick-Introduction-to-Space-Engineers-Ingame-Scripts
        //
        // to learn more about ingame scripts.



        // START OF USER CODE //

        // CUSTOM TYPES //

        struct actionDelay_t
        {
            public string actionArg;
            public long delayTime;
            public actionDelay_t(string arg, long time)
            {
                actionArg = arg;
                delayTime = time;
            }
        }
        
        // MY GLOBALS HERE //
        string storedArgument = "";
        long ticks = 0;
        long sec = 0;
        float secF = 0f;
        List<actionDelay_t> delayBuffer = new List<actionDelay_t>();
        bool debug = false;
        bool powerManagerEnabled = false;
        bool breachDetectionEnabled = false;
        bool watchdogEnabled = false;



        // USER-DEFINED FUNCTIONS //

        // Matches LCDs (as defined in their Custom Info field) with their respectively named blocks
        Dictionary<IMyTerminalBlock, IMyTextSurface> MatchLCDsToBlocks(List<IMyTerminalBlock> blockList, List<IMyTextSurface> LCD_list)
        {
            Dictionary<IMyTerminalBlock, IMyTextSurface> keyMap = new Dictionary<IMyTerminalBlock, IMyTextSurface>();

            foreach (IMyTextSurface lcd in LCD_list)
            {
                string lcdTarget = ((IMyTerminalBlock)lcd).CustomData;
                if (keyMap.ContainsKey((IMyTerminalBlock)lcd) || lcdTarget.Length == 0) continue;
                foreach(IMyTerminalBlock block in blockList)
                {
                    string blockRef = ((IMyTerminalBlock)block).CustomData;
                    if (blockRef.Length == 0) continue;
                    if (String.Compare(lcdTarget, blockRef) == 0)
                    {
                        keyMap.Add(block, lcd);
                        // blockList.Remove(block);
                        // LCD_list.Remove(lcd);
                        break;
                    }
                }
            }
            return keyMap;
        }

        string[] storeDelays(List<actionDelay_t> delays)
        {
            string[] tokens = {"delaysBegin"};

            if (delays.Count > 0)
            {
                foreach (actionDelay_t actionDelay in delays)
                {
                    tokens.Append(actionDelay.actionArg);
                    tokens.Append(actionDelay.delayTime.ToString());
                }
            }

            tokens.Append("delaysEnd");
            return tokens;
        }

        // END USER-DEFINED FUNCTIONS //



        public Program()
        {
            // The constructor, called only once every session and
            // always before any other method is called. Use it to
            // initialize your script. 
            //     
            // The constructor is optional and can be removed if not
            // needed.
            // 
            // It's recommended to set Runtime.UpdateFrequency 
            // here, which will allow your script to run itself without a 
            // timer block.

            Runtime.UpdateFrequency = (UpdateFrequency)(1); // DEFAULT UPDATE FREQ. OF ONCE PER ONE (1) TICK(S)
        }

        public void Save()
        {
            // Called when the program needs to save its state. Use
            // this method to save your state to the Storage field
            // or some other means. 
            // 
            // This method is optional and can be removed if not
            // needed.

            Storage =  string.Join(";",
                storedArgument ?? "",
                ticks.ToString(),
                sec.ToString(),
                secF.ToString(),
                storeDelays(delayBuffer)
            );
        }

        public void Main(string argument, UpdateType updateSource)
        {
            // The main entry point of the script, invoked every time
            // one of the programmable block's Run actions are invoked,
            // or the script updates itself. The updateSource argument
            // describes where the update came from. Be aware that the
            // updateSource is a  bitfield  and might contain more than 
            // one update type.
            // 
            // The method itself is required, but the arguments above
            // can be removed if not needed.



            // LOCAL INIT //

            // Loading Stored Data //
            string storedArgument = "";
            long storedTicks = 0;
            long storedSec = 0;
            float storedSecF = 0;
            List<actionDelay_t> storedDelays = new List<actionDelay_t>();

            string[] storedData = Storage.Split(';');
            if (storedData.Length > 0)
            {
                storedArgument = storedData[0];
                long.TryParse(storedData[1], out storedTicks);
                long.TryParse(storedData[2], out storedSec);
                float.TryParse(storedData[3], out storedSecF);
                if (storedData[4] == "delaysBegin")
                {
                    for (int i = 5; storedData[i] != "delaysEnd"; i = i + 2)
                    {
                        long thisDelayTime = 0;
                        if (long.TryParse(storedData[i+1], out thisDelayTime))
                        {
                            actionDelay_t thisDelay = new actionDelay_t(storedData[i], thisDelayTime);
                            storedDelays.Add(thisDelay);
                        }
                    }
                }
            }
            else
            {
                storedArgument = "";
                storedTicks = 0;
                storedSec = 0;
                storedSecF = 0f;
                storedDelays = null;
            }

            // Argument Handler //
            string actionCall = "";
            if (updateSource == UpdateType.Trigger && argument.Length > 0 && argument.ToLower().Contains("action:"))
            {
                actionCall = argument.Substring("action:".Length, argument.Length - "action:".Length);
            }
            else if (argument.Length > 0)
            {
                storedArgument = argument;
                string[] args = argument.Split(' ');

                foreach (string arg in args)
                {
                    switch(arg)
                    {
                        case "debug":
                            debug = true;
                            break;
                        case "powerManagerEnabled":
                            powerManagerEnabled = true;
                            break;
                        case "breachDetectionEnabled":
                            breachDetectionEnabled = true;
                            break;
                        case "watchdogEnabled":
                            watchdogEnabled = true;
                            break;
                        default:
                            break;
                    }
                }
            } else if (storedArgument != ""){
                string[] args = storedArgument.Split(' ');

                foreach (string arg in args)
                {
                    switch(arg)
                    {
                        case "debug":
                            debug = true;
                            break;
                        case "powerManagerEnabled":
                            powerManagerEnabled = true;
                            break;
                        case "breachDetectionEnabled":
                            breachDetectionEnabled = true;
                            break;
                        case "watchdogEnabled":
                            watchdogEnabled = true;
                            break;
                        default:
                            break;
                    }
                }
            }

            // Core Debug //
            Echo(Me.CustomName);
            ticks++;
            secF = ticks / 60f;
            sec = ticks / 60;
            if (debug)
            {
                Echo("Ticks passed: " + ticks);
                Echo("Time Elapsed: " + secF.ToString("0.00") + " seconds");
                Echo("Debug: ON");
                Echo("Power Manager: " + (powerManagerEnabled ? "ON" : "OFF"));
                Echo("Breach Detection: " + (breachDetectionEnabled ? "ON" : "OFF"));
                Echo("Watchdog Timer: " + (watchdogEnabled ? "ON" : "OFF"));
            }


            // Base Asthetics //
            VRageMath.Color green = new VRageMath.Color(0, 255, 0);

            IMyTextSurface mainComputerMonitor = (IMyTextSurface)Me.GetSurface(0);
            mainComputerMonitor.ContentType = (ContentType)(1);
            mainComputerMonitor.FontColor = green;
            mainComputerMonitor.FontSize = 0.75f;
            IMyCubeGrid thisGrid = (IMyCubeGrid)Me.CubeGrid;

            // Grid block list
            List<IMyTerminalBlock> allTerminalBlocks = new List<IMyTerminalBlock>();
            GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(allTerminalBlocks);

            // LCD block list
            List<IMyTextSurface> allLCDs = new List<IMyTextSurface>();
            GridTerminalSystem.GetBlocksOfType<IMyTextSurface>(allLCDs);

            // Dictionary of blocks with monitor LCDs
            Dictionary<IMyTerminalBlock, IMyTextSurface> monitorKeys = MatchLCDsToBlocks(allTerminalBlocks, allLCDs);

            // END LOCAL INIT //



            // LOCAL DISPLAY //

            mainComputerMonitor.WriteText("root/XBE/" + thisGrid.CustomName + "/~",false);
            if (Me.IsRunning)
            {
                mainComputerMonitor.WriteText("\n\nAllSystemsGood: Program Running...\nTime Elapsed: " + (sec / 3600).ToString("00") + ":" + ((sec / 60) % 60).ToString("00") + ":" + (sec % 60).ToString("00"), true);
                mainComputerMonitor.WriteText("\n\nModules:\n  Debug: " + (debug ? "ON" : "OFF"), true);
                mainComputerMonitor.WriteText("\n  Power Manager: " + (powerManagerEnabled ? "ON" : "OFF"), true);
                mainComputerMonitor.WriteText("\n  Breach Detection: " + (breachDetectionEnabled ? "ON" : "OFF"), true);
                mainComputerMonitor.WriteText("\n  Watchdog Timer: " + (watchdogEnabled ? "ON" : "OFF"), true);
            }
            else
            {
                mainComputerMonitor.WriteText("\n\nAllSystemsGood: Program Halted", true);
            }

            // END LOCAL DISPLAY //



            // POWER CONSUMPTION //

            float powerConsumed = 0; // Power consumption in megawatts

            foreach (IMyTerminalBlock block in allTerminalBlocks)
            {
                bool isConsumer = false;
                float powerConsumedBlock = 0f;
                char[] separators = {' ','\n','\t',':'};
                string[] tokens = block.DetailedInfo.Split(separators);
                string tokenLast = "";
                foreach (string token in tokens)
                {
                    if ((String.Compare(token.ToLower(),"consumed") == 0 || String.Compare(token.ToLower(),"consumption") == 0) && String.Compare(tokenLast.ToLower(),"power") == 0) isConsumer = true;
                    if (String.Compare(tokenLast.ToLower(),"power") != 0 && isConsumer && float.TryParse(token, out powerConsumedBlock))
                    {
                        powerConsumed += powerConsumedBlock;
                        isConsumer = false;
                        break;
                    }
                }
            }

            // END POWER CONSUMPTION //



            // SOLAR PANELS //

            List<IMySolarPanel> solarPanels = new List<IMySolarPanel>();
            GridTerminalSystem.GetBlocksOfType<IMySolarPanel>(solarPanels);

            float solarPanelsOutput = 0f;
            foreach (IMySolarPanel solarPanel in solarPanels)
            {
                solarPanelsOutput = solarPanel.CurrentOutput;
            }

            // END SOLAR PANELS //



            // BATTERIES CONTROL //

            List<IMyBatteryBlock> batteries = new List<IMyBatteryBlock>();
            GridTerminalSystem.GetBlocksOfType<IMyBatteryBlock>(batteries);

            float batteriesStoredPower = 0f;
            float batteriesOutput = 0f;
            foreach (IMyBatteryBlock battery in batteries)
            {
                batteriesStoredPower += battery.CurrentStoredPower;
                batteriesOutput += battery.CurrentOutput;
            }

            // END BATTERIES CONTROL //



            // REACTOR CONTROL //

            List<IMyReactor> reactors = new List<IMyReactor>();
            GridTerminalSystem.GetBlocksOfType<IMyReactor>(reactors);

            float minTimeForBackup = 6f; // Hours
            if (((batteriesStoredPower / batteriesOutput) < minTimeForBackup || (solarPanelsOutput + batteriesOutput) <= powerConsumed) && powerManagerEnabled)
            {
                foreach (IMyReactor reactor in reactors)
                {
                    reactor.Enabled = true;
                }
            } else if (powerManagerEnabled) {
                foreach (IMyReactor reactor in reactors)
                {
                    reactor.Enabled = false;
                }
            } else {
                // Don't manage reactor power
            }

            // END REACTOR CONTROL //



            // REACTOR MONITORING //

            foreach (IMyReactor reactor in reactors)
            {
                IMyTextSurface reactor_monitor; //monitorKeys[(IMyTerminalBlock)reactor];
                if (!monitorKeys.TryGetValue((IMyTerminalBlock)reactor, out reactor_monitor)) continue;
                reactor_monitor.ContentType = (ContentType)(1);
                reactor_monitor.FontColor = green;
                reactor_monitor.FontSize = 1.25f;
                
                reactor_monitor.WriteText("Status: " + (reactor.Enabled ? "ON" : "OFF"), false);
                if (reactor.Enabled == true)
                {
                    reactor_monitor.WriteText("\nCurrent Output: " + ((reactor.CurrentOutput < 1f) ? ((reactor.CurrentOutput * 1000).ToString("0.0") + " kW") : ((reactor.CurrentOutput).ToString("0.000") + " MW")), true);

                    string loadGraphic = "";
                    for (int i = 0; i < 15; i++)
                    {
                        if (i * 20 < reactor.CurrentOutput)
                        {
                            loadGraphic += "+";
                        } else {
                            loadGraphic += "=";
                        }
                    }

                    reactor_monitor.WriteText("\nLoad: [" + loadGraphic + "]", true);
                } else {
                    // "Status: OFF", no further text
                }
            }

            // END REACTOR MONITORING //



            // HULL BREACH PROTOCOLS //

            // Room-Doors key-value pairs
            Dictionary<IMyBlockGroup, List<IMyDoor>> doorRoomKeys = new Dictionary<IMyBlockGroup, List<IMyDoor>>();

            // List of all doors on grid
            List<IMyDoor> allDoors = new List<IMyDoor>();
            GridTerminalSystem.GetBlocksOfType<IMyDoor>(allDoors);

            // List of all groups containing the string "Air Vents"
            List<IMyBlockGroup> airVentGroups = new List<IMyBlockGroup>();
            Func<IMyBlockGroup, bool> checkIfVentGroup = group => group.Name.ToLower().Contains("air vents");
            GridTerminalSystem.GetBlockGroups(airVentGroups, checkIfVentGroup);
            
            if (debug) Echo("\n\nRooms found: ");

            foreach (IMyBlockGroup group in airVentGroups)
            {
                // String of air vent group after removing "Air Vents - " from group name
                string ventGroupPrefix = "Air Vents - ";
                string roomName = group.Name.Substring(ventGroupPrefix.Length, group.Name.Length - ventGroupPrefix.Length).ToLower();

                if (debug) Echo(roomName);

                // List of doors connecting to this room
                List<IMyDoor> inRoom = new List<IMyDoor>();

                // Add this door to inRoom list if room is listed in CustomData of door
                foreach (IMyDoor door in allDoors)
                {
                    if (((IMyTerminalBlock)door).CustomData.ToLower().Contains(roomName))
                    {
                        inRoom.Add(door);
                    }
                    // If no room name is found, we ignore this door
                }

                // Add key (vent group) with value inRoom (list of doors connecting room)
                doorRoomKeys.Add(group, inRoom);
            }

            // Room keys (air vent group) with breach status value
            Dictionary<IMyBlockGroup,bool> breachDetected = new Dictionary<IMyBlockGroup,bool>();

            // Check each room (vent group)
            foreach (IMyBlockGroup ventGroup in airVentGroups)
            {
                // Add each vent group to breachDetected dictionary
                breachDetected.Add(ventGroup, false);
                // List of vents in this room
                List<IMyAirVent> groupVents = new List<IMyAirVent>();
                ventGroup.GetBlocksOfType<IMyAirVent>(groupVents);

                // Check each vent in group for a breach
                foreach (IMyAirVent vent in groupVents)
                {
                    // Breach conditions: Can't pressurize room (leak), attempting pressurization (unintentional depressurization), and block is functional (not WIP in an incomplete room/area)
                    if (!vent.CanPressurize && !vent.Depressurize && vent.IsFunctional)
                    {
                        // This room must have a breach
                        if (breachDetected.ContainsKey(ventGroup)) breachDetected[ventGroup] = true;
                        // Break out of foreach loop to prevent overwrite of breachDetected state
                        break;
                    }
                    // Room doesn't have a breach, reset breachDetected flag
                    else if (breachDetected.ContainsKey(ventGroup)) breachDetected[ventGroup] = false;
                }

                // If auto-detection is enabled and we found a breach
                if (doorRoomKeys.ContainsKey(ventGroup) && breachDetected.ContainsKey(ventGroup) && breachDetected[ventGroup] && breachDetectionEnabled)
                {
                    // Get all doors connecting to this room
                    List<IMyDoor> doors = new List<IMyDoor>();
                    doorRoomKeys.TryGetValue(ventGroup, out doors);
                    // Go through each door on grid
                    foreach (IMyDoor door in doors)
                    {
                        // If this door connects to the breached room, close it to seal the breach off from other rooms
                        door.CloseDoor();
                    }
                }
            }

            if (debug)
            {
                Echo("\nBreach status:");
                foreach (KeyValuePair<IMyBlockGroup, bool> status in breachDetected)
                {
                    Echo("\n\t" + status.Key + ":\t" + (status.Value ? "Breach" : "Sealed"));
                }
            }

            // END HULL BREACH PROTOCOLS //



            // REFRESH WATCHDOG TIMER //

            List<IMyTimerBlock> timers = new List<IMyTimerBlock>();
            GridTerminalSystem.GetBlocksOfType<IMyTimerBlock>(timers);
            IMyTimerBlock watchdog = null;
            
            foreach (IMyTimerBlock timer in timers)
            {
                if (((IMyTerminalBlock)timer).CustomData.ToLower().Contains("watchdog")) watchdog = timer;
            }
            
            if (watchdogEnabled && watchdog != null)
            {
                watchdog.Silent = true;
                watchdog.TriggerDelay = 5; // seconds
                watchdog.StartCountdown(); // Resets WDT each tick
            }

            // END REFRESH WATCHDOG TIMER //



            // AUTO-AIRLOCKS //

            // Dictionary of airlock sequence initialization buttons (key) and their respective airlock rooms/groups (value)
            Dictionary<IMyButtonPanel, IMyBlockGroup> airlockButtons = new Dictionary<IMyButtonPanel, IMyBlockGroup>();

            // List of all airlock button panel blocks
            List<IMyButtonPanel> airlockPanels = new List<IMyButtonPanel>();
            Func<IMyButtonPanel,bool> selectAirlockButtonPanels = panel => panel.CustomData.ToLower().Contains("airlock");
            GridTerminalSystem.GetBlocksOfType<IMyButtonPanel>(airlockPanels, selectAirlockButtonPanels);

            // Match button panels to airlock rooms/doors
            foreach (IMyButtonPanel panel in airlockPanels)
            {
                string info = panel.CustomData.ToLower();
                
                foreach (IMyBlockGroup group in airVentGroups)
                {
                    // String of air vent group after removing "Air Vents - " from group name
                    string ventGroupPrefix = "Air Vents - ";
                    string roomName = group.Name.Substring(ventGroupPrefix.Length, group.Name.Length - ventGroupPrefix.Length).ToLower();

                    // Compare room names
                    if (roomName.ToLower().Contains("airlock") && info.Contains(roomName))
                    {
                        airlockButtons.Add(panel, group);
                        break;
                    }
                }

                if (actionCall.Contains("airlock"))
                {
                    // Add airlock control sequence here
                }
            }

            // END AUTO-AIRLOCKS //
        }
        // END OF USER CODE //
    }
}


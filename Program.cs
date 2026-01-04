using EmptyKeys.UserInterface.Generated;
using Sandbox.Definitions;
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
using System.Runtime.InteropServices;
using System.Security.Permissions;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.Network;
using VRage.Scripting.MemorySafeTypes;
using VRageMath;
using VRageRender.Utils;

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



        // SET & ADJUST FEATURE SETTINGS HERE //

        // Number of seconds before any fully opened, non-hangar doors will automatically close (default: 10 seconds)
        ulong closeDoorsTime = 10;

        // Number of seconds before fully opened airlock doors will close during the Auto-Airlock cycle (default: 5 seconds)
        ulong airlockDoorDelayTime = 5;
        // Prefix for any air vent groups on the grid
        string ventGroupPrefix = "Air Vents - ";

        // END OF USER-FRIENDLY SETTINGS //



        // TIME-KEEPING SETTINGS //

        const ulong ticksPerRun = 5;
        const ulong ticksPerSec = 60;
        const double secToRuns = (double)ticksPerSec;// / (double)ticksPerRun;
        const double msPerRun = (1000d / ticksPerSec) * ticksPerRun;

        // END TIME-KEEPING SETTINGS //



        // CUSTOM TYPES //

        public class ActionDelay
        {
            public string actionArg;
            public ulong delayTime;
            public bool Tick()
            {
                if (delayTime == 0) return false;
                ulong oldTime = delayTime;
                --delayTime;
                return oldTime > delayTime;
            }
            public ActionDelay(string arg, double timeInSec)
            {
                actionArg = arg;
                delayTime = (ulong)(timeInSec * secToRuns);
            }
        }

        enum AirlockStatus
        {
            ERROR = -1,
            INACTIVE = 0,
            ACTIVE_IN_SEAL = 1,
            ACTIVE_IN_START = 2,
            ACTIVE_IN_PRESS = 3,
            ACTIVE_IN_END = 4,
            ACTIVE_OUT_START = 5,
            ACTIVE_OUT_SEAL = 6,
            ACTIVE_OUT_DEPRESS = 7,
            ACTIVE_OUT_END = 8,
        };

        struct Room
        {
            // Public member variables
            public string name;
            public List<IMyAirVent> vents;
            public List<IMyDoor> doors;
            // Private member variables
            public Dictionary<IMyDoor,string> roomConnections;

            // Public member functions
            public bool IsExternal(IMyDoor door)
            {
                if (doors.Contains(door) && roomConnections.ContainsKey(door))
                {
                    return roomConnections[door] == "Exterior";
                }
                else return false;
            }

            // Constructors
            public Room(string roomName = "", List<IMyAirVent> roomVents = null, List<IMyDoor> roomDoors = null)
            {
                name = roomName;
                vents = roomVents;
                doors = roomDoors;
                roomConnections = null;
                
                foreach (IMyDoor door in doors)
                {
                    char[] roomNameDelimiters = {';',':',','};
                    string[] roomNames = door.CustomData.Split(roomNameDelimiters);
                    if (roomNames.Length > 2) continue;
                    else
                    {
                        foreach (string room in roomNames)
                        {
                            if (room == name) continue;
                            else roomConnections.Add(door, room);
                        }
                    }
                }
            }
        }

        // END CUSTOM TYPES //


        
        // MY GLOBALS HERE //

        string storedArgument = "";
        ulong storedDataLength = 5;
        ulong ticks = 0;
        ulong sec = 0;
        float secF = 0f;
        List<ActionDelay> delayBuffer = new List<ActionDelay>();
        string actionCall = "";
        List<Room> rooms = new List<Room>();
        bool debug = false;
        bool powerManagerEnabled = true;
        bool breachDetectionEnabled = true;
        bool watchdogEnabled = true;
        bool autoAirlocksEnabled = true;
        bool closeDoorsEnabled = true;
        bool execTimeExceeded = false;
        char[] majorDelim = {':','\n'};
        char[] minorDelim = {',','\t','='};
        VRageMath.Color green = new VRageMath.Color(0, 255, 0);

        // END MY GLOBALS //



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
                        break;
                    }
                }
            }
            return keyMap;
        }

        int compareDelays(ActionDelay delay1, ActionDelay delay2)
        {
            return delay1.delayTime.CompareTo(delay2.delayTime);
        }

        bool addDelay(ActionDelay newAction, ref List<ActionDelay> delays)
        {
            List<ActionDelay> newDelays = new List<ActionDelay>(delays);
            // bool atEnd = false;

            // if (newDelays.Count == 0) newDelays.Add(newAction);
            // else
            // {
            //     ActionDelay indexToAdd = newDelays.First();
            //     foreach (ActionDelay action in newDelays)
            //     {
            //         if (newAction.delayTime < action.delayTime)
            //         {
            //             indexToAdd = action;
            //             break;
            //         }
            //         else if (action.Equals(newDelays.Last()))
            //         {
            //             atEnd = true;
            //         }
            //     }

            //     if (atEnd) newDelays.Add(newAction);
            //     else newDelays.Insert(delays.IndexOf(indexToAdd), newAction);
            // }

            delays.Add(newAction);
            delays.Sort(compareDelays);
            
            if (newDelays != delays)
            {
                // delays = newDelays;
                return true;
            }
            
            return false;
        }

        bool addDelaysList(List<ActionDelay> newActions, ref List<ActionDelay> delays)
        {
            bool status = true;
            foreach (ActionDelay delay in newActions)
            {
                addDelay(delay, ref delays);
                if (!delays.Contains(delay)) status = false;
            }
            return status;
        }

        bool removeDelayList(List<ActionDelay> oldActions, ref List<ActionDelay> delays)
        {
            bool status = true;
            foreach (ActionDelay delay in oldActions)
            {
                if (delays.Contains(delay)) delays.Remove(delay);
                if (delays.Contains(delay)) status = false;
            }
            return status;
        }

        string storeDelays(List<ActionDelay> delays)
        {
            string tokens = "delaysBegin";

            if (delays.Count > 0)
            {
                foreach (ActionDelay actionDelay in delays) tokens += ":" + actionDelay.actionArg + ',' + actionDelay.delayTime.ToString() + ":";
            }
            else tokens += ":";

            tokens += "delaysEnd";
            return tokens;
        }

        Room GetRoomFromName(string searchName, List<Room> searchRooms)
        {
            foreach (Room room in searchRooms)
            {
                if (searchName.ToLower() == room.name.ToLower()) return room;
            }
            return new Room();
        }

        // END USER-DEFINED FUNCTIONS //



        // FEATURE FUNCTIONS //

        bool argHandler(string argument, UpdateType updateSource)
        {
            if (updateSource == UpdateType.Trigger && argument.Length > 0 && argument.ToLower().Contains("action:"))
            {
                actionCall = argument.Substring("action:".Length, argument.Length - "action:".Length);
                Runtime.UpdateFrequency = (UpdateFrequency)(ticksPerRun);
                argHandler(storedArgument, UpdateType.Terminal);
                return true;
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
                        case "powerManagerDisabled":
                            powerManagerEnabled = false;
                            break;
                        case "breachDetectionDisabled":
                            breachDetectionEnabled = false;
                            break;
                        case "watchdogDisabled":
                            watchdogEnabled = false;
                            break;
                        case "autoAirlocksDisabled":
                            autoAirlocksEnabled = false;
                            break;
                        case "closeDoorsDisabled":
                            closeDoorsEnabled = false;
                            break;
                        default:
                            break;
                    }
                }
                return true;
            }
            else if (storedArgument != "")
            {
                string[] args = storedArgument.Split(' ');

                foreach (string arg in args)
                {
                    switch(arg)
                    {
                        case "debug":
                            debug = true;
                            break;
                        case "powerManagerDisabled":
                            powerManagerEnabled = false;
                            break;
                        case "breachDetectionDisabled":
                            breachDetectionEnabled = false;
                            break;
                        case "watchdogDisabled":
                            watchdogEnabled = false;
                            break;
                        case "autoAirlocksDisabled":
                            autoAirlocksEnabled = false;
                            break;
                        case "closeDoorsDisabled":
                            closeDoorsEnabled = false;
                            break;
                        default:
                            break;
                    }
                }
                return true;
            }
            return false;
        }

        bool powerManagement(List<IMyTerminalBlock> allTerminalBlocks, Dictionary<IMyTerminalBlock,IMyTextSurface> monitorKeys, ref List<ActionDelay> delays)
        {
            if (powerManagerEnabled)
            {

                // POWER CONSUMPTION //

                float powerConsumed = 0; // Power consumption in megawatts

                foreach (IMyTerminalBlock block in allTerminalBlocks)
                {
                    char[] separators = {'\n','\t',':'};
                    string[] tokens = block.DetailedInfo.Split(separators);
                    string tokenLast = "";
                    foreach (string token in tokens)
                    {
                        float powerConsumedBlock = 0f;
                        if (float.TryParse(token, out powerConsumedBlock) && String.Compare(tokenLast.Trim(),"Required Input") == 0) continue;
                        else if (token.Trim().ToUpper().Contains("MW"))
                        {
                            powerConsumed += powerConsumedBlock;
                            break;
                        }
                        else if (token.Trim().ToUpper().Contains("KW"))
                        {
                            powerConsumed += powerConsumedBlock / 1000;
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

                if ((batteriesStoredPower / batteriesOutput) < minTimeForBackup || (solarPanelsOutput + batteriesOutput) <= powerConsumed)
                {
                    foreach (IMyReactor reactor in reactors)
                    {
                        reactor.Enabled = true;
                    }
                }
                else
                {
                    foreach (IMyReactor reactor in reactors)
                    {
                        reactor.Enabled = false;
                    }
                }

                // END REACTOR CONTROL //



                // REACTOR MONITORING //

                foreach (IMyReactor reactor in reactors)
                {
                    IMyTextSurface reactor_monitor;
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

                

                // POWER USAGE & HEURISTICS //

                // Maximum output power for complete grid
                double maxPowerOutput = 0d;
                List<IMyPowerProducer> powerProducers = new List<IMyPowerProducer>();
                GridTerminalSystem.GetBlocksOfType<IMyPowerProducer>(powerProducers);

                foreach (IMyPowerProducer producer in powerProducers) maxPowerOutput += producer.MaxOutput;

                List<IMyTextSurfaceProvider> allBlockDisplays = new List<IMyTextSurfaceProvider>();
                GridTerminalSystem.GetBlocksOfType<IMyTextSurfaceProvider>(allBlockDisplays);

                foreach (IMyTextSurfaceProvider block in allBlockDisplays)
                {
                    if (((IMyTerminalBlock)block).CustomData.ToLower().Contains("heuristics"))
                    {
                        int pwrUsageSurface = 0, pwrStorageSurface = 0;
                        string[] tokens = ((IMyTerminalBlock)block).CustomData.Split(majorDelim);
                        if (tokens.Length > 1)
                        {
                            string tokenLast = "";
                            foreach (string token in tokens)
                            {
                                if (tokenLast.ToLower().Contains("powerusage") && int.TryParse(token, out pwrUsageSurface))
                                {
                                    // Print out Power Usage heuristics here!
                                    /*
                                    _
                                     |
                                     |
                                     |
                                     |
                                    _|
                                     |
                                     |
                                     |
                                     |
                                    _|
                                     |              ______
                                     |       ______|======|
                                     |      |//////|======|
                                     |______|//////|======|
                                    _|//////|//////|======|
                                     |//////|//////|======|
                                     |//////|//////|======|
                                     |//////|//////|======|
                                     |//////|//////|======|
                                    _|______|______|======|______|______|______|______|______|______|______|

                                    */
                                }
                                if (tokenLast.ToLower().Contains("powerstorage") && int.TryParse(token, out pwrStorageSurface))
                                {
                                    // Print out Power Storage heuristics here!
                                }

                                tokenLast = token;
                            }
                        }
                    }
                }

                // END POWER USAGE & HEURISTICS //

                return true;
            }
            return false;
        }

        bool breachDetection(List<IMyBlockGroup> airVentGroups, Dictionary<IMyBlockGroup,List<IMyDoor>> doorRoomKeys)
        {
            if (breachDetectionEnabled)
            {
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

                    // If we found a breach
                    if (doorRoomKeys.ContainsKey(ventGroup) && breachDetected.ContainsKey(ventGroup) && breachDetected[ventGroup])
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
                        Echo("  - " + status.Key.ToString().Split('-')[1].Trim() + ": " + (status.Value ? "Breach" : "Sealed"));
                    }
                }
            }
            return true;
        }

        bool watchdogTimer()
        {
            List<IMyTimerBlock> timers = new List<IMyTimerBlock>();
            GridTerminalSystem.GetBlocksOfType<IMyTimerBlock>(timers);
            IMyTimerBlock watchdog = null;
            
            if (watchdogEnabled)
            {
                foreach (IMyTimerBlock timer in timers)
                {
                    if (((IMyTerminalBlock)timer).CustomData.ToLower().Contains("watchdog:" + Me.CustomName.ToLower())) watchdog = timer;
                }
                
                if (watchdogEnabled && watchdog != null)
                {
                    watchdog.Silent = true;
                    watchdog.TriggerDelay = 5; // seconds
                    watchdog.StartCountdown(); // Resets WDT each tick
                }
            }
            return true;
        }

        bool autoAirlocks(List<IMyBlockGroup> airVentGroups, Dictionary<IMyBlockGroup,List<IMyDoor>> doorRoomKeys, ref List<ActionDelay> delayBuffer)
        {
            if (autoAirlocksEnabled)
            {
                List<ActionDelay> removeList = new List<ActionDelay>();

                // If we've hit a button calling the Auto-Airlock action, add to buffer with no delay
                if (actionCall.Contains("Auto-Airlocks:")) addDelay(new ActionDelay(actionCall, 0), ref delayBuffer);
                
                // Checks each group if active airlock and continues/starts cycle if so
                foreach (ActionDelay delay in delayBuffer)
                {
                    string room = "";
                    AirlockStatus status = AirlockStatus.INACTIVE;
                    if (delay.actionArg.Contains("Auto-Airlocks:") && delay.delayTime == 0)
                    {
                        // Get arguments pairs in call
                        string[] actionArgs = delay.actionArg.Split(majorDelim);
                        foreach (string arg in actionArgs)
                        {
                            // Split argument commands from parameters
                            string[] tokens = arg.Split(minorDelim);
                            // If the argument is missing parameters or empty
                            if (tokens.Length <= 1 && !arg.Contains("Auto-Airlocks"))
                            {
                                // Error noted and cancel behavior
                                status = AirlockStatus.ERROR;
                                break;
                            }
                            // Else, parse commands
                            switch(tokens[0].ToLower())
                            {
                                // Sets airlock room to be cycled
                                case "room":
                                    room = tokens[1].ToLower();
                                    break;
                                // Determines direction (going into or coming out of pressurized side) and state of cycle
                                case "state":
                                    switch(tokens[1].ToLower())
                                    {
                                        case "ext_start":
                                            status = AirlockStatus.ACTIVE_IN_START;
                                            break;
                                        case "int_start":
                                            status = AirlockStatus.ACTIVE_OUT_START;
                                            break;
                                        case "ext_seal":
                                            status = AirlockStatus.ACTIVE_IN_SEAL;
                                            break;
                                        case "int_seal":
                                            status = AirlockStatus.ACTIVE_OUT_SEAL;
                                            break;
                                        case "ext_press":
                                            status = AirlockStatus.ACTIVE_IN_PRESS;
                                            break;
                                        case "int_depress":
                                            status = AirlockStatus.ACTIVE_OUT_DEPRESS;
                                            break;
                                        case "ext_end":
                                            status = AirlockStatus.ACTIVE_IN_END;
                                            break;
                                        case "int_end":
                                            status = AirlockStatus.ACTIVE_OUT_END;
                                            break;
                                        default:
                                            break;
                                    }
                                    break;
                                // If no valid command found, error is noted
                                default:
                                    status = AirlockStatus.ERROR;
                                    break;
                            }
                        }
                    }

                    // Check if all O2 tanks are full to allow exception in cycling if can't depressurize
                    List<IMyGasTank> o2Tanks = new List<IMyGasTank>();
                    GridTerminalSystem.GetBlocksOfType<IMyGasTank>(o2Tanks);
                    float emptyO2Capacity = 0f;
                    foreach (IMyGasTank tank in o2Tanks)
                    {
                        string type = tank.DetailedInfo.Split('\n')[0].Split(':')[1].Trim();
                        if (type.Contains("Oxygen"))
                        {
                            emptyO2Capacity += (float)(1 - tank.FilledRatio) * tank.Capacity;
                        }
                        // else o2Tanks.Remove(tank);
                    }

                    foreach (IMyBlockGroup group in airVentGroups)
                    {
                        string roomName = group.Name.Substring(ventGroupPrefix.Length, group.Name.Length - ventGroupPrefix.Length).ToLower();
                        if (delay.actionArg.ToLower().Contains(roomName))
                        {
                            List<IMyDoor> doors = new List<IMyDoor>();
                            List<IMyDoor> intDoors = new List<IMyDoor>();
                            List<IMyDoor> extDoors = new List<IMyDoor>();
                            // Get interior and exterior doors lists
                            if (doorRoomKeys.TryGetValue(group, out doors))
                            {
                                foreach (IMyDoor door in doors)
                                {
                                    if (door.CustomData.ToLower().Contains("exterior")) extDoors.Add(door);
                                    else if (door.CustomData.ToLower().Contains(roomName)) intDoors.Add(door);
                                }
                            }

                            List<IMyTerminalBlock> vents = new List<IMyTerminalBlock>();
                            group.GetBlocks(vents);
                            bool ready = true;
                            // State-based action in cycle
                            switch(status)
                            {
                                case AirlockStatus.ACTIVE_IN_START:
                                    foreach (IMyDoor intDoor in intDoors)
                                    {
                                        if (!intDoor.Closed)
                                        {
                                            intDoor.CloseDoor();
                                            ready = false;
                                        }
                                    }
                                    if (!ready) break;
                                    foreach (IMyDoor extDoor in extDoors)
                                    {
                                        if (extDoor.OpenRatio < 1)
                                        {
                                            extDoor.OpenDoor();
                                            ready = false;
                                        }
                                    }
                                    if (ready)
                                    {
                                        addDelay(new ActionDelay(delay.actionArg.Replace("ext_start", "ext_seal"), airlockDoorDelayTime), ref delayBuffer);
                                        removeList.Add(delay);
                                    }
                                    break;
                                case AirlockStatus.ACTIVE_OUT_START:
                                    foreach (IMyDoor extDoor in extDoors)
                                    {
                                        if (!extDoor.Closed)
                                        {
                                            extDoor.CloseDoor();
                                            ready = false;
                                        }
                                    }
                                    if (!ready) break;
                                    foreach (IMyDoor intDoor in intDoors)
                                    {
                                        if (intDoor.OpenRatio < 1)
                                        {
                                            intDoor.OpenDoor();
                                            ready = false;
                                        }
                                    }
                                    if (ready)
                                    {
                                        addDelay(new ActionDelay(delay.actionArg.Replace("int_start", "int_seal"), airlockDoorDelayTime), ref delayBuffer);
                                        removeList.Add(delay);
                                    }
                                    break;
                                case AirlockStatus.ACTIVE_IN_SEAL:
                                    foreach (IMyDoor door in extDoors)
                                    {
                                        if (!door.Closed)
                                        {
                                            door.CloseDoor();
                                            ready = false;
                                        }
                                    }
                                    if (ready)
                                    {
                                        addDelay(new ActionDelay(delay.actionArg.Replace("ext_seal", "ext_press"), 0), ref delayBuffer);
                                        removeList.Add(delay);
                                    }
                                    break;
                                case AirlockStatus.ACTIVE_OUT_SEAL:
                                    foreach (IMyDoor door in intDoors)
                                    {
                                        if (!door.Closed)
                                        {
                                            door.CloseDoor();
                                            ready = false;
                                        }
                                    }
                                    if (ready)
                                    {
                                        addDelay(new ActionDelay(delay.actionArg.Replace("int_seal", "int_depress"), 0), ref delayBuffer);
                                        removeList.Add(delay);
                                    }
                                    break;
                                case AirlockStatus.ACTIVE_IN_PRESS:
                                    foreach (IMyAirVent vent in vents)
                                    {
                                        vent.Depressurize = false;
                                        if (vent.Status == VentStatus.Depressurized)
                                        {
                                            ready = false;
                                        }
                                    }
                                    if (ready)
                                    {
                                        addDelay(new ActionDelay(delay.actionArg.Replace("ext_press", "ext_end"), 0), ref delayBuffer);
                                        removeList.Add(delay);
                                    }
                                    break;
                                case AirlockStatus.ACTIVE_OUT_DEPRESS:
                                    foreach (IMyAirVent vent in vents)
                                    {
                                        vent.Depressurize = true;
                                        if (vent.Status != VentStatus.Depressurized && emptyO2Capacity > 0)
                                        {
                                            ready = false;
                                        }
                                    }
                                    if (ready)
                                    {
                                        addDelay(new ActionDelay(delay.actionArg.Replace("int_depress", "int_end"), 0), ref delayBuffer);
                                        removeList.Add(delay);
                                    }
                                    break;
                                case AirlockStatus.ACTIVE_IN_END:
                                    foreach (IMyDoor door in intDoors)
                                    {
                                        if (door.Closed)
                                        {
                                            door.OpenDoor();
                                            ready = false;
                                        }
                                    }
                                    if (ready)
                                    {
                                        removeList.Add(delay);
                                    }
                                    break;
                                case AirlockStatus.ACTIVE_OUT_END:
                                    foreach (IMyDoor door in extDoors)
                                    {
                                        if (door.Closed)
                                        {
                                            door.OpenDoor();
                                            ready = false;
                                        }
                                    }
                                    if (ready)
                                    {
                                        removeList.Add(delay);
                                    }
                                    break;
                                default:
                                    break;
                            }
                        }
                    }
                }

                removeDelayList(removeList, ref delayBuffer);
            }
            return true;
        }

        bool closeDoors(List<IMyDoor> allDoors, ref List<ActionDelay> delayBuffer)
        {
            if (closeDoorsEnabled)
            {
                List<ActionDelay> removeList = new List<ActionDelay>();
                // Check each door
                foreach (IMyDoor door in allDoors)
                {
                    // State: whether door needs to be closed NOW
                    // bool needsClosing = false;
                    // State: whether closing action is already queued
                    bool isQueued = false;

                    // If not a hangar door and not excluded via CustomData tag
                    if (!door.CustomName.ToLower().Contains("hangar door") && !door.CustomData.ToLower().Contains("doorclosing=off"))
                    {
                        // If open, this door will need to be closed
                        if (door.OpenRatio == 1)
                        {
                            // needsClosing = true;
                            // Check if we've already queued a closing action
                            foreach (ActionDelay actionDelay in delayBuffer)
                            {
                                // If we have already queued a closing action
                                if (actionDelay.actionArg.ToLower().Contains(door.CustomName.ToString().ToLower()))
                                {
                                    isQueued = true;
                                    if (debug)
                                    {
                                        // string[] lines = door.CustomData.Split('\n');
                                        // if (lines.Length < 2) door.CustomData += "\nClosing in: " + (actionDelay.delayTime / secToRuns).ToString("0.00") + " seconds";
                                        // else
                                        // {
                                        //     lines[1] = "Closing in: " + (actionDelay.delayTime / secToRuns).ToString("0.00") + " seconds";
                                        //     door.CustomData = String.Join("\n", lines);
                                        // }
                                        IMyTextSurface mainMonitor = (IMyTextSurface)Me.GetSurface(0);
                                        mainMonitor.WriteText("\nClosing " + door.CustomName + " in: " + (actionDelay.delayTime / secToRuns).ToString("0.00") + " seconds", true);
                                    }
                                    // And it's not set for NOW
                                    if (actionDelay.delayTime > 0)
                                    {
                                        // Don't close yet
                                        // needsClosing = false;
                                    }
                                    // But if it is set for NOW, remove the action from buffer
                                    else
                                    {
                                        removeList.Add(actionDelay);
                                        // If we've determined that this door should be closed, do so now
                                        door.CloseDoor();
                                    }
                                    break;
                                }
                            }

                            // If a needed closing action is not yet queued, add it to the buffer
                            if (!isQueued) addDelay(new ActionDelay("closeDoor:" + door.CustomName.ToString(), closeDoorsTime), ref delayBuffer);
                        }
                        else
                        {
                            // If door isn't fully open but is queued to close, remove from delayBuffer
                            foreach (ActionDelay actionDelay in delayBuffer)
                            {
                                if (actionDelay.actionArg.ToLower().Contains(door.CustomName.ToString().ToLower())) removeList.Add(actionDelay);
                            }
                        }

                        // NOTE: If door is closed BEFORE closing delay expires, then needsClosing == false
                    }
                }
                removeDelayList(removeList, ref delayBuffer);
            }
            return true;
        }

        bool cleanupBuffer(ref List<ActionDelay> delayBuffer)
        {
            string delayDebugInfo = "";
            // Debug delay buffer
            if (debug)
            {
                delayDebugInfo = "Delays:";
                ulong delayCount = 0;
                ActionDelay debugDelay = null;
                bool hasDebugDelay = false;
                foreach (ActionDelay delay in delayBuffer)
                {
                    delayDebugInfo += "\n"+ ++delayCount + ")\n" + delay.actionArg + "\n" + delay.delayTime + " ticks\n" + ((float)delay.delayTime / secToRuns).ToString("0.00") + " seconds";
                    if (delay.actionArg == "debugDelay")
                    {
                        debugDelay = delay;
                        hasDebugDelay = true;
                    }
                }
                
                Echo("\nDelay Buffer: " + (hasDebugDelay ? "OPERATIONAL" : "COMPROMISED"));
                if (!hasDebugDelay) addDelay(new ActionDelay("debugDelay", 1), ref delayBuffer);
                else if (debugDelay != null && debugDelay.delayTime == 0)
                {
                    delayBuffer.Remove(debugDelay);
                    addDelay(new ActionDelay("debugDelay", 1), ref delayBuffer);
                }
            }

            // Ensure buffer is sorted
            delayBuffer.Sort(compareDelays);

            if (debug)
            {
                Echo("Number of active delays: " + delayBuffer.Count());
                Echo("\n" + delayDebugInfo);
            }

            // Check for missed actions in buffer
            // for (int i = 0; i < delayBuffer.Count; i++)
            // {
            //     // if (delayBuffer[i].delayTime == 0) Echo("ERROR: ACTION \"" + delayBuffer[i].actionArg + "\" FAILED TO EXECUTE");
            //     // else delayBuffer[i] = new ActionDelay(delayBuffer[i].actionArg, delayBuffer[i].delayTime - 1);
            //     delayBuffer[i].delayTime = 1;
            //     delayBuffer[i] = new ActionDelay(delayBuffer[i].actionArg, delayBuffer[i].delayTime - 1);
            // }

            bool status = true;

            for (int i = 0; i < delayBuffer.Count; i++)
            {
                ulong prevTime = delayBuffer[i].delayTime;
                if (delayBuffer[i].delayTime > 0) --delayBuffer[i].delayTime;
                status = prevTime > delayBuffer[i].delayTime;
                if (debug) Echo("\nTick " + delayBuffer[i].actionArg + " " + (status ? "succeeded" : "failed"));
            }

            return true;
        }

        // END FEATURE FUNCTIONS //













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

            Runtime.UpdateFrequency = (UpdateFrequency)(ticksPerRun); // DEFAULT UPDATE FREQ. OF ONCE PER ONE (1) TICK(S)
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
            ulong storedTicks = 0;
            ulong storedSec = 0;
            float storedSecF = 0;
            List<ActionDelay> storedDelays = new List<ActionDelay>();

            string[] storedData = Storage.Split(';');
            
            if (storedData.Length >= (int)storedDataLength && ticks == 0 && argument.Length == 0)
            {
                storedArgument = storedData[0];
                ulong.TryParse(storedData[1], out storedTicks);
                ulong.TryParse(storedData[2], out storedSec);
                float.TryParse(storedData[3], out storedSecF);
                string[] storedDelayStrings = storedData[4].Split(':');
                foreach (string str in storedDelayStrings)
                {
                    if (str.CompareTo("delaysBegin") != 0 && str.CompareTo("delaysEnd") != 0)
                    {
                        string[] delayArgs = str.Split(',');
                        ulong thisDelayTime = 0;
                        if (delayArgs.Length == 2)
                        {
                            if (delayArgs[0].Length > 0 && ulong.TryParse(delayArgs[1], out thisDelayTime)) addDelay(new ActionDelay(delayArgs[0], thisDelayTime), ref storedDelays);
                        }
                    }
                }
                storedDelays.Sort(compareDelays);

                // ticks = storedTicks;
                // sec = storedSec;
                // secF = storedSecF;
                // delayBuffer = storedDelays;
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
            argHandler(argument, updateSource);

            // Core Debug //
            Echo(Me.CustomName);
            ticks++;
            secF = ticks / (float)ticksPerSec;
            sec = ticks / ticksPerSec;
            if (Runtime.LastRunTimeMs > msPerRun) execTimeExceeded = true;
            if (debug)
            {
                Echo("Ticks passed: " + ticks);
                Echo("Time elapsed: " + secF.ToString("0.00") + " seconds");
                Echo("Script execution time exceeded: " + (execTimeExceeded ? "YES" : "NO"));
                Echo("Last execution time: " + Runtime.LastRunTimeMs.ToString("0.00") + " ms");
                Echo("Debug: ON");
                Echo("Power Manager: " + (powerManagerEnabled ? "ON" : "OFF"));
                Echo("Breach Detection: " + (breachDetectionEnabled ? "ON" : "OFF"));
                Echo("Watchdog Timer: " + (watchdogEnabled ? "ON" : "OFF"));
                Echo("Auto-Airlocks: " + (autoAirlocksEnabled ? "ON" : "OFF"));
                Echo("Close Doors: " + (closeDoorsEnabled ? "ON" : "OFF"));
            }


            // Base Asthetics //
            IMyTextSurface mainComputerMonitor = (IMyTextSurface)Me.GetSurface(0);
            mainComputerMonitor.ContentType = (ContentType)(1);
            mainComputerMonitor.FontColor = green;
            mainComputerMonitor.FontSize = 0.75f;
            VRage.Game.ModAPI.Ingame.IMyCubeGrid thisGrid = (VRage.Game.ModAPI.Ingame.IMyCubeGrid)Me.CubeGrid;

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
                mainComputerMonitor.WriteText("\n  Auto-Airlocks: " + (autoAirlocksEnabled ? "ON" : "OFF"), true);
                mainComputerMonitor.WriteText("\n  Close Doors: " + (closeDoorsEnabled ? "ON" : "OFF"), true);
            }
            else
            {
                mainComputerMonitor.WriteText("\n\nAllSystemsGood: Program Halted", true);
            }

            // END LOCAL DISPLAY //



            // POWER MANAGEMENT //
            powerManagement(allTerminalBlocks, monitorKeys, ref delayBuffer);
            // END POWER MANAGEMENT //



            // Room-Doors key-value pairs
            Dictionary<IMyBlockGroup, List<IMyDoor>> doorRoomKeys = new Dictionary<IMyBlockGroup, List<IMyDoor>>();

            // List of all doors on grid
            List<IMyDoor> allDoors = new List<IMyDoor>();
            GridTerminalSystem.GetBlocksOfType<IMyDoor>(allDoors);

            // List of all groups containing the string "Air Vents"
            List<IMyBlockGroup> airVentGroups = new List<IMyBlockGroup>();
            Func<IMyBlockGroup, bool> checkIfVentGroup = group => group.Name.ToLower().Contains("air vents");
            GridTerminalSystem.GetBlockGroups(airVentGroups, checkIfVentGroup);
            
            if (debug) Echo("\n\nRooms found:");

            int groupCount = 0;
            foreach (IMyBlockGroup group in airVentGroups)
            {
                groupCount++;
                // String of air vent group after removing prefix from group name
                string roomName = group.Name.Substring(ventGroupPrefix.Length, group.Name.Length - ventGroupPrefix.Length);

                if (debug) Echo("  " + groupCount + ") " + roomName);

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



            // HULL BREACH PROTOCOLS //
            breachDetection(airVentGroups, doorRoomKeys);
            // END HULL BREACH PROTOCOLS //



            // REFRESH WATCHDOG TIMER //
            watchdogTimer();
            // END REFRESH WATCHDOG TIMER //



            // AUTO-AIRLOCKS //
            autoAirlocks(airVentGroups, doorRoomKeys, ref delayBuffer);
            // END AUTO-AIRLOCKS //



            // CLOSE DOORS //
            closeDoors(allDoors, ref delayBuffer);
            // END CLOSE DOORS //


            // DELAY BUFFER CLEANUP & DEBUG //
            cleanupBuffer(ref delayBuffer);
            // END BUFFER CLEANUP & DEBUG //

        }
        // END OF USER CODE //
    }
}


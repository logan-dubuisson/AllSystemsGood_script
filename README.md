# AllSystemsGood_script
AllSystemsGood is a general purpose Space Engineers script designed to maximize automation of basic (and often tedious) tasks, such as power management, atmospheric breach closure, and more. The goal of this script is to provide Engineers with a higher degree of base autonomy with a simple implementation so they can focus on the important stuff (like not dying!).

In order to select features you'd like to use, simply add the value show in (argument = "value") next to your desired features to the arguments list in the programmable block with a space between each (e.g., add "powerManagerEnabled watchdogEnabled" if you only want the Power Management and Watchdog Timer features enabled).

Current features include:

- Power Management (argument = powerManagerEnabled)
        Controls ON/OFF state of all reactors on a grid based on solar panel and battery power output and stored energy. Also allows for LCD monitor feedback for power info.

- Breach Detection & Sealing (argument = breachDetectionEnabled)
        Detects breaches in pressurized rooms and automatically seals all doors to/from said room(s) to prevent depressurization of more areas. Does not affect rooms that are set to depressurize, such as airlocks or hangars.

- Watchdog Timer (argument = watchdogEnabled)
        Allows user to link a watchdog timer (WDT) that can automatically restart the script if an issue occurs that stops execution.

- Debug (argument = debug) & Programmable Block Screen Info (always on)
        Shows useful info that can aid in debugging setup or provide feedback on program runtime info such as time elapsed and run state.
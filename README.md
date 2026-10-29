# RTS for Programmers

![Unity 6](https://img.shields.io/badge/Unity%206-100000?style=for-the-badge&logo=unity&logoColor=white)

> [!WARNING]
> This is a WIP prototype.

Multiplayer RTS combat game with programming. You have to write scripts for all of your units/buildings using [Vibrissa](https://github.com/banszkyy/Vibrissa).

## Controls

### Menus

- `ESC` : Pause menu / Close any opened menu
- `B` : Buildings
- `U` : Units
- `ENTER`: Open chat / Send message

### Camera

- `Middle-click` + drag : Rotate camera
- `SHIFT` + `Middle-click` + drag: Move camera
- `CTRL` + `Middle-click` + drag: Zoom camera
- `W`, `A`, `S`, `D` : Move camera
- `Numpad +`, `Numpad -` : Zoom camera

### Selection

- `Left-click` on unit : Select unit
- `SHIFT` + `Left-click` on unit : Add/Remove unit to selection
- `Left-click` + drag : Selection box --> Select units
- `SHIFT` + `Left-click` + drag : Selection box --> Add units to selection

### Units

- `Right-click` on unselected unit : Open the unit's menu (only if there's no units selected)
- `Right-click` : Open commands menu (only if there are units selected and those have commands)

## Programming

### Uploading a script into a unit

1. Place your `.vi` script into the `StreamingAssets` directory, which is located in the installed game's directory.
    > The standard library and example scripts are also located here.
2. Open the unit's menu
3. Navigate to Processor > Source
4. Click on the folder button ("Select File", with a folder icon)
5. Choose your script
6. Click on the compile button ("Compile", with a hammer icon)

After compilation the processor starts automatically

### Version Locking

All the units that also have this script uploaded, will also restart and will use the newly compiled program. To prevent this, you can lock a unit's script to never update when the script changes.

1. Open the unit's menu
2. Navigate to Processor > Source
3. Click on the lock button ("Lock Source", with a lock icon)

### Hot Reload

> [!CAUTION]
> The compiler will not check for possible conflicts, so the processor can also just crash. In this case, you have to restart the processor manually.
> A successful hot-reload happens when the compiled code's size didn't change - like changing a numeric literal.

1. Open the unit's menu
2. Navigate to Processor > Source
3. Click on the hot reload button ("Hot Reload", with a fire/flame icon)

### LED Indicators

- Main LED :
  - Off : Processor has no script uploaded
  - Green : Processor is running
  - Yellow : Processor is halted (paused)
  - Red : Processor is crashed / Compilation failed
- Custom LED is controlled by code
- Wireless Send & Wireless Receive LEDs : The corresponding LED blinks once if a transmission was sent/received.
- Radar LED : Blinks once if the radar was used

### Debugging

There's a VSCode extension that allows you to debug any of your unit.

1. Open the unit you want to debug
2. Navigate to the Processor tab
3. Click on the debug button ("Debug", with a bug icon)
4. VSCode will automatically open and will attach to the game, debugging your unit

#### IMPORTANT NOTICES

The game actively listents on the following ports:
- 8052 - Extension Host (LSP Server)
- 8053 - Debugger Host (DAP Server)

Currently debugging only works locally (singleplayer, or host mode).
In the future, you'll have to expose specific port ranges (with port-forwarding, for example).

#### Not Important Notices

Normally you can pause/continue/restart any unit with the corresponding buttons.
When you're debugging a unit, the behavior changes in expected ways:

- Pause - Will pause the unit, triggering a stop in the debugger too
- Continue - Will continue the unit, and also the debugger
- Restart - Will restart the unit, and also the debugger

Also note that when the unit is paused (when you're stepping statement by statement using the debugger), the unit cannot process any incoming transmission packets. If your identification system uses transmissions, it might trigger a false-negative.

### API

You can figure it out from the example scripts.

Some interface is provided by call based API-s and some are memory mapped.
The availability depends on the unit type.

## Cheats

> [!NOTE]
> These are only avaliable if you're an **admin** (you're the host, or in singleplayer mode).

- `/creative` : Enters creative mode (buildings & units can be placed down immediately without consuming resources)
- `/research all|<string>` : Researches all or the specified technology instantly

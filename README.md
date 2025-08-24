# Rocket League Auto Drop Opener (WPF)

## Description

This repository contains a C# WPF application that automates the Rocket League drop opening process.  
It provides a modern user interface, fixed hotkeys, automatic delay handling, and visual feedback on whether the game is detected.

## Features

- Automatic drop opening in Rocket League.
- Fixed hotkeys (**F4 = Start**, **F5 = Stop/Exit**).
- Automatic delay detection:  
  - **With plugin → ~4 s**  
  - **Without plugin → ~8 s**  
- Built-in game process detection (`RocketLeague.exe`).
- One-click BakkesMod plugin installer/ updater (`Assets/Plugins/`).
- Compact and modern UI with PinkBro artwork integrated into the background.

## How it Works

1. Start **Rocket League**.  
2. *(For fastest opening)* enter **Free Play / Training once**, then return to the menu.  
3. Go to **Garage → Manage Inventory → Reward Items tab**.  
4. Hover your mouse over the **Open Drop** button.  
5. Press **F4** to begin automatic drop opening.  
6. Press **F5** to stop or close the program.  

⚠️ Make sure you are on the correct screen before starting.  

## Version

Version 1.1.0

## Author

- **Author:** PinkBro

## Note

This application is developed solely for educational purposes and personal use.  
The use of automation software in games may violate the game's terms of service.  
Use it responsibly and in accordance with the game's rules.

## Getting Started

1. Clone or download this repository.  
2. Open the project in Visual Studio 2022 (with .NET Desktop Development workload).  
3. Build the project (`Release` mode recommended).  
4. Run the `.exe` file from `bin/Release/net8.0-windows`.

## License

This project is licensed under the [MIT License](LICENSE).

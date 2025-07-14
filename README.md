# Daz Content Installer

A utility for managing and installing third-party Daz 3D Content. This application helps you keep track of your content library, install new items, and see what you have installed.

## Features

*   **Install Content:** Easily install content from compressed archives (`.zip`, `.7z`, etc.).
*   **Content Library:** Keeps a database of your installed content for easy management.
*   **Status Tracking:** See which archives have been installed.
*   **Cross-Platform:** Built with Avalonia, allowing it to run on Windows, macOS, and Linux.

## Screenshots

![UI Overview](/assets/ui.png)
![Library Config](/assets/settings-1.png)
![Settings](/assets/settings-2.png)

## Built With

This project is built with .NET 9 and Avalonia UI. Key dependencies include:

*   **[.NET 9](https://dotnet.microsoft.com/en-us/download/dotnet/9.0)** - The underlying framework.
*   **[Avalonia](https://avaloniaui.net/)** - A cross-platform UI framework for .NET.
*   **[ReactiveUI](https://reactiveui.net/)** - An MVVM framework for .NET.
*   **[Entity Framework Core (SQLite)](https://docs.microsoft.com/en-us/ef/core/)** - For the local database.
*   **[SharpSevenZip](https://github.com/JeremyAnsel/SharpSevenZip)** - For handling various archive formats.

## Download & Run

Pre-built releases for Windows are available on the [Releases page](https://github.com/TheSeventhCode/daz-content-installer/releases).

1. **Download**  
   Go to the [Releases](https://github.com/TheSeventhCode/daz-content-installer/releases) section and download the latest `.zip` file for your system:
   - `DazContentInstaller-win-x64.zip` for most modern Windows PCs (64-bit)
   - `DazContentInstaller-win-x86.zip` for older 32-bit Windows systems

2. **Extract**  
   Unzip the downloaded file to a folder of your choice.

3. **Keep files together**  
    Make sure to keep the extracted files always together, as the application executable depends on those few libraries.

4. **Run**  
   Double-click `DazContentInstaller.exe` inside the extracted folder to start the application. No installation is required.

> **Note:**  
> macOS and Linux users will need to build from source (see the Development section below).

## Archive Compatibility

Not all asset archives will work out of the box. Different people use different ways how they package and distribute their assets. The installer is designed to handle the typical Daz 3D content structure—usually, this means an archive containing a `Content` directory (or similar) with subfolders like `data`, `Runtime`, `People`, etc. Archives following this structure should install easily.

If you encounter an archive that doesn't work as expected, please [create an issue](https://github.com/TheSeventhCode/daz-content-installer/issues) so I can take a look and improve compatibility!

## Development

To build and run this project from source, you will need the .NET 9 SDK installed.

1.  **Clone the repository:**
    ```sh
    git clone https://github.com/TheSeventhCode/daz-content-installer.git
    cd daz-content-installer
    ```

2.  **Restore dependencies:**
    ```sh
    dotnet restore DazContentInstaller/DazContentInstaller.csproj
    ```

3.  **Run the application:**
    ```sh
    dotnet run --project DazContentInstaller/DazContentInstaller.csproj
    ```

## License

This project is licensed under the **GNU General Public License v3.0**. See the [LICENSE](LICENSE) file for details. 
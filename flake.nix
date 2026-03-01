{
  description = "Development shell for Daz Content Installer";

  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";
    flake-utils.url = "github:numtide/flake-utils";
  };

  outputs = { self, nixpkgs, flake-utils }:
    flake-utils.lib.eachDefaultSystem (system:
      let
        pkgs = import nixpkgs { inherit system; };
        runtimeLibs = with pkgs;
          pkgs.lib.optionals pkgs.stdenv.hostPlatform.isLinux [
            alsa-lib
            fontconfig
            freetype
            icu
            libGL
            libpulseaudio
            libxkbcommon
            openssl
            sqlite
            stdenv.cc.cc
            wayland
            libice
            libsm
            libx11
            libxcursor
            libxext
            libxfixes
            libxi
            libxinerama
            libxrandr
            libxrender
            libxtst
            zlib
          ];
        updateDepsCommand = pkgs.writeShellScriptBin "update-dci-deps" ''
           nix shell nixpkgs#dotnet-sdk_9 nixpkgs#nuget-to-json -c sh -c '
                rm -rf ./.nuget-packages
                dotnet restore "DazContentInstaller/DazContentInstaller.csproj" --packages ./.nuget-packages
                nuget-to-json ./.nuget-packages > deps.json
                rm -rf ./.nuget-packages
              '
          '';
      in
      {
        devShells.default = pkgs.mkShell {
          packages = with pkgs; [
            dotnet-sdk_9
            patchelf
            nuget-to-json
            updateDepsCommand
          ];
        };

        packages.daz-content-installer = pkgs.buildDotnetModule rec {
          pname = "daz-content-installer";
          version = "0.1.0";
          src = ./.;

          projectFile = "DazContentInstaller/DazContentInstaller.csproj";
          nugetDeps = ./deps.json;

          dotnet-sdk = pkgs.dotnetCorePackages.sdk_9_0;
          dotnet-runtime = pkgs.dotnetCorePackages.runtime_9_0;

          selfContainedBuild = true;
          executables = [ "DazContentInstaller" ];
          runtimeDeps = runtimeLibs;

          meta = with pkgs.lib; {
            description = "Avalonia GUI installer for DAZ content";
            homepage = "https://github.com/theanachronism/daz-content-installer";
            license = licenses.mit;
            mainProgram = "DazContentInstaller";
            platforms = platforms.linux;
          };
        };

        packages.default = self.packages.${system}.daz-content-installer;

        apps.default = {
          type = "app";
          program = "${self.packages.${system}.daz-content-installer}/bin/DazContentInstaller";
        };
      });
}
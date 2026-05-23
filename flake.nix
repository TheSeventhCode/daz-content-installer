{
  description = "Nix flake for Daz Content Installer";

  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";
    flake-utils.url = "github:numtide/flake-utils";
  };

  outputs = { self, nixpkgs, flake-utils }:
    flake-utils.lib.eachDefaultSystem (system:
      let
        pkgs = import nixpkgs { inherit system; };
        lib = pkgs.lib;
        fontconfigFile = "${pkgs.fontconfig.out}/etc/fonts/fonts.conf";
        fontconfigPath = "${pkgs.fontconfig.out}/etc/fonts";
        runtimeLibs = with pkgs;
          lib.optionals pkgs.stdenv.hostPlatform.isLinux [
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
        runtimeLibraryPath = lib.makeLibraryPath runtimeLibs;
        runtimeEnvironment = ''
          export LD_LIBRARY_PATH="${runtimeLibraryPath}:''${LD_LIBRARY_PATH:-}"
          export FONTCONFIG_FILE="${fontconfigFile}"
          export FONTCONFIG_PATH="${fontconfigPath}"
        '';
        wrapperArgs = [
          "--prefix"
          "LD_LIBRARY_PATH"
          ":"
          runtimeLibraryPath
          "--set"
          "FONTCONFIG_FILE"
          fontconfigFile
          "--set"
          "FONTCONFIG_PATH"
          fontconfigPath
        ];
        cleanSrc = lib.cleanSourceWith {
          src = ./.;
          filter = path: type:
            let
              baseName = baseNameOf path;
            in
              !(
                lib.elem baseName [
                  ".direnv"
                  ".idea"
                  ".nuget-packages"
                  "bundle-extract"
                  "bundle-extract-ok"
                  "publish"
                  "result"
                ]
                || (type == "directory" && lib.elem baseName [ "bin" "obj" ])
              );
        };
        updateDepsCommand = pkgs.writeShellScriptBin "update-dci-deps" ''
          set -euo pipefail

          repo_root="$(git rev-parse --show-toplevel 2>/dev/null || pwd)"
          cd "$repo_root"

          rm -rf ./.nuget-packages
          dotnet restore "DazContentInstaller/DazContentInstaller.csproj" --packages ./.nuget-packages
          nuget-to-json ./.nuget-packages > deps.json
          rm -rf ./.nuget-packages
        '';
        writeRiderEnv = pkgs.writeShellScriptBin "write-dci-rider-env" ''
          out_file="''${1:-.rider.env}"

          cat > "$out_file" <<EOF
LD_LIBRARY_PATH=${runtimeLibraryPath}
FONTCONFIG_FILE=${fontconfigFile}
FONTCONFIG_PATH=${fontconfigPath}
EOF

          echo "Wrote $out_file"
        '';
        desktopItem = pkgs.makeDesktopItem {
          name = "daz-content-installer";
          desktopName = "DAZ Content Installer";
          genericName = "DAZ Content Installer";
          comment = "Avalonia desktop installer for third-party DAZ content";
          exec = "DazContentInstaller";
          icon = "daz-content-installer";
          startupWMClass = "DazContentInstaller";
          terminal = false;
          categories = [ "Utility" ];
        };
      in
      {
        devShells.default = pkgs.mkShell {
          packages = with pkgs; [
            dotnet-sdk_10
            patchelf
            nuget-to-json
            updateDepsCommand
            writeRiderEnv
          ];

          shellHook = runtimeEnvironment;
        };

        packages.daz-content-installer = pkgs.buildDotnetModule {
          pname = "daz-content-installer";
          version = "0.1.0";
          src = cleanSrc;

          projectFile = "DazContentInstaller/DazContentInstaller.csproj";
          nugetDeps = ./deps.json;

          dotnet-sdk = pkgs.dotnetCorePackages.sdk_10_0;
          dotnet-runtime = pkgs.dotnetCorePackages.runtime_10_0;
          nativeBuildInputs = [ pkgs.copyDesktopItems pkgs.makeWrapper ];
          desktopItems = [ desktopItem ];

          selfContainedBuild = true;
          executables = [ "DazContentInstaller" ];
          runtimeDeps = runtimeLibs;
          postInstall = ''
            install -Dm644 "$NIX_BUILD_TOP/source/DazContentInstaller/Assets/icon.svg" \
              "$out/share/icons/hicolor/scalable/apps/daz-content-installer.svg"
          '';
          postFixup = ''
            wrapProgram "$out/bin/DazContentInstaller" ${lib.escapeShellArgs wrapperArgs}
          '';

          meta = with lib; {
            description = "Avalonia desktop installer for third-party DAZ content";
            homepage = "https://github.com/TheSeventhCode/daz-content-installer";
            license = licenses.gpl3Only;
            mainProgram = "DazContentInstaller";
            platforms = platforms.linux;
          };
        };

        packages.default = self.packages.${system}.daz-content-installer;

        apps.daz-content-installer = {
          type = "app";
          program = "${self.packages.${system}.daz-content-installer}/bin/DazContentInstaller";
        };
        apps.default = self.apps.${system}.daz-content-installer;
      });
}

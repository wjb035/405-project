<p align="center">
  <img src="/assets/images/catui/banner_f.png" alt="CATui Header">
</p>

# CATui: Emulation Frontend

CATui is an emulation frontend built with the **Godot Engine 4.6**. It utilizes Libretro-based cores and runs all emulators within a single process, featuring social integration. This repository contains the source code for the client application.

> Status: **developer ALPHA** - structure and code are subject to frequent changes.
---

## Getting Started

### How to Run (For Developers)

To run the source code, the setup is straightforward as this is a Godot project.

#### Prerequisites
1.  **Godot Engine:** You must have **Godot Engine v4.6** installed. Download it from the official Godot website.
2.  **Git:** To clone the repository.

#### Setup and Execution
1.  **Clone the Repository:**
	```bash
	git clone [https://github.com/arthiee4/CATui.git](https://github.com/arthiee4/CATui.git)
	cd CATui
	```
2.  **Open and Run:**
	* Launch the Godot Engine **v4.6**.
	* Click **"Import"** and select the `project.godot` file on the clone rep location.

---

## Emulator Setup

CATui uses **libretro cores** to run games. You need to download the core DLLs and configure the paths.

### Supported Consoles

...

### Core Setup

To play games, you need to import the correct Libretro Core for your platform.

1.  **Download Core**: Go to the Libretro Buildbot and download the core for your OS:
    *   [Libretro Nightly Buildbot](https://buildbot.libretro.com/nightly/)
    *   **Windows:** navigate to `windows/x86_64/latest/` and download `.dll` files (e.g., `mgba_libretro.dll`).
    *   **Linux:** (Untested) navigate to `linux/x86_64/latest/` and download `.so` files.
    *   **Android:** navigate to `android/latest/` and download `.so` files. Use `armeabi-v7a` for older devices and `arm64-v8a` for newer devices.

2.  **Import in CATui**:
    *   Open CATui settings.
    *   Go to **Emulation**.
    *   Select **Import Core Manually**.
    *   Choose the core file you downloaded (`.dll` or `.so`).

The emulator will automatically register the core and it will be ready to use!



### Building and Exporting

To generate a distributable executable (the 'compilation' step in Godot):

* **Note:** Soon a detailed guide. Since CATui is in *developer alpha*, there is no final, usable build or export configuration ready for general distribution. This section will be completed before the first public release.

---

## License

Licensing information will be published with the first public release of the project.

---

## CATui – Legal Notice & Disclaimer

CATui is an open-source, libre emulation frontend that allows users to play their own legally obtained game backups on **ANY** device.

CATui does not contain, distribute, or provide, or link to any copyrighted games, ROMs, BIOS files, or encryption keys.

CATui only works with game files (ROMs/ISOs) that you have legally created yourself from cartridges or discs you personally own (known as "dumping"). Using CATui with illegally downloaded games constitutes copyright infringement and strictly violates our terms of use.

We fully respect intellectual property rights and comply with all applicable laws, including Brazilian copyright law (Lei nº 9.610/1998) and international treaties.

By using CATui, you confirm that:
* You will only use game files you have legally created from your own physical media you own.
* You understand that downloading or sharing copyrighted games without permission is illegal.

This software is provided under the GNU GPL license and follows the same legal principles that have allowed projects such as RetroArch, PPSSPP, Dolphin, DuckStation, and Lemuroid to operate safely for over a decade.

Thank you for respecting copyright and supporting game preservation.

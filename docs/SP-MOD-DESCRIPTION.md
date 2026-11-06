# Multipurpose Drone [Beta]

Scout a route before you commit. Launch Mechanic’s MS-AR4144, switch to the camera in your MS Glasses, and use zoom, night vision or thermal imaging to see what’s ahead. Your character stays where you left them and can still be attacked.

> Open **F1 → Multipurpose Drone** to adjust the drone’s audio, camera and flight settings.

[Watch the drone demo](https://www.youtube.com/watch?v=F-koeMzecKk)

## Deploy your first drone

1. Buy the **MS-AR4144 Drone Kit** from Mechanic at Loyalty Level 1 for **₽65,000**. The base limit is **50 per restock**.
2. Right-click the kit and choose **Unpack**. Each box contains a drone, **MS Glasses**, and an **MS-Remote**. Keep the three matching serial numbers together.
3. Wear the glasses, place the remote in a special slot, and bind the remote to a quickslot key.
4. Drop the drone on clear ground nearby and press your bound remote key. Climb with **E** to lift off.

## Flight and camera controls

| Input | Action |
| --- | --- |
| Mouse and WASD | Aim and steer |
| Q / E | Descend / climb |
| Your sprint binding | Toggle normal and fast flight |
| Right-click | Cycle the upper-right zoom inset through Off, 10×, 20× and 30× |
| Mouse wheel | Cycle normal, night vision and thermal |
| Left-click | Dive toward a tracked target. Click again to cancel. |
| F10 or remove the glasses | Return to your character. The drone hovers and still uses battery. |

The drone has a ten-minute battery and 100 durability. AI can shoot it down. A dive can detonate on a target or an obstacle, so watch the approach and keep clear of the blast. Water contact can destroy the drone. When a purchased drone is lost, its matching remote and glasses can drop near your character.

## Reading the HUD

| Where | What it tells you |
| --- | --- |
| Upper left | Radio signal. Four bars through 200 m, three through 300 m, two through 350 m, and one through 400 m. Static gets more frequent as the signal weakens. Beyond 400 m the link can drop. At 450 m it cuts out. Move closer to reconnect. |
| Top centre | Compass heading in degrees |
| Upper right | Battery percentage and the zoom inset when enabled. The inset magnifies the centre reticle. |
| Left and right edges | Current speed in km/h and height above the surface below, in metres |
| Centre | Reticle and distance to the surface or object it points at. Green boxes mark visible tracked AI in normal view. Night vision hides the boxes. |
| Lower left | Camera pitch and drone durability |
| Lower right | Flight mode, battery time remaining, durability, connection state and target status |

## F1 settings

| Setting | What it changes |
| --- | --- |
| Enable prototype | Turns the drone client features on or off |
| Viewing equipment | Selects the viewing equipment mode |
| Drone sound effects volume | Adjusts rotor and landing sounds immediately. Default 25%. Set to 0 to mute. |
| Fisheye | Toggles lens distortion. Off by default. |
| Flight speed | Sets base flight speed. Default 6 m/s. |
| Limit radio range / Maximum range | Applies an optional flight range cap. Off by default, with 300 m stored as the cap. |
| Shortcuts | Change the fallback controller, night vision and disconnect keys |
| Allow F9 test deployment | Allows gear-free deployment for beta testing |

## Installation and compatibility

Close the game and SPT server. Extract the release ZIP into your SPT folder so **BepInEx/** and **SPT_Runtime/** merge with the existing folders. Use the client and server files from the same release. F1 opens Configuration Manager when available. The settings file is BepInEx/config/com.killdg.multipurposedrone.cfg.

The beta targets **solo SPT 4.1.6** and is standalone. Fika and other mod combinations are unverified. Map geometry and detail can load late in the remote camera, particularly during fast flight. If you hit a problem, include the build version, map, your other mods and how to reproduce it.

> is it realistic? not really, is it what they said might get included? who knows, just wanted to see what it would be like floating around a raid

Created by **killdg** with ChatGPT assistance.

[Support the project on Ko-fi](https://ko-fi.com/killdg)

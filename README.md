
# ![AnoMech](images/icon.png) AnoMech

*Another FFXIV mechanics simulator*

---
 
Simulate FFXIV raid mechanics client-side for solo practice. Go to any Inn, open the plugin with `/anomech` and start practicing!

Thanks to improvemnts by [WorstAquaPlayer](https://github.com/WorstAquaPlayer) plugin is quite stable now! No more 
crashes after training session.

**WARNING!!!**

**You are cut off from server traffic while in the sim zone.** To keep the
fake zone stable, the plugin firewalls incoming packets from the server.  
While simulating:
  * Players joining or leaving your party will not appear in the party list
  until you leave the sim zone.
  * Ready checks will not pop.
 


### Splatoon, BossMod and NyaDraw

With [Splatoon](https://github.com/PunishXIV/Splatoon) installed, its layouts (presets) and scripts
for the simulated fight work inside a scenario, and BossMod (and BossMod Reborn) and NyaDraw see
the sim's head markers and tethers. Toggle it under `/anomech config` →
**Plugin compatibility** (on by default).

Boss casts, action effects, head markers, tethers, statuses and arena map effects already
reach these plugins, because the sim plays them through the same game functions and packet
handlers the server's packets go through. While a scenario runs, AnoMech also sets the
encounter state the firewall would otherwise block:

* **Zone.** Dalamud only learns the zone from the server, so it would keep reporting the inn.
  AnoMech sets it to the simulated fight's territory on entry and back to the inn on leave, so
  zone-locked layouts and scripts load.
* **In combat.** Enables combat-only layouts and `Controller.CombatSeconds`, and resets
  Splatoon's scripts between pulls. A restart drops combat for 0.4 s first, so Splatoon (and
  cactbot) see a new pull.
* **In duty.** Enables duty-only layouts.
* **Party.** The bots are already in the game's party list. While you're in a cross-world
  party, though, Splatoon reads the server's cross-world roster instead, so AnoMech hides
  the cross-world flag during a run. Priority lists and scripts then see the bots, named
  after their job (`Warrior`, `White Mage`, ...) on your world. Role-mode lists with
  **Fill automatically** work best, because which job the bots get depends on your own.
* **Phase scene.** Sets the scene that scripts check with `Controller.Scene` and that layout
  scene locks use: TOP P2 = 3, TOP P5 = 6, TOP P6 = 7, Dancing Mad P2 = 7, Dancing Mad P3 = 8. Other
  phases keep the zone's own value. Use **Scene override** to force a scene for them.
* **BossMod's module.** BossMod's fight modules follow the fight from the pull, so a scenario
  that starts mid-fight would leave them in an earlier phase. With **Move BossMod's module to
  the scenario's start** on, AnoMech moves the module to the scenario's mechanic where one is
  mapped (Dancing Mad P3 Black Hole and Stomp-a-Mole). This also gives NyaDraw its BossMod AOEs.

Known gaps:
* The scene is only known for the phases above.
* NPC chat lines (`OnMessage` with boss dialogue, BossMod/NyaDraw NPC yells) aren't sent.
  `OnDirectorUpdate` only sees the director updates the sim replays itself, such as Commence at
  zone load. It never sees a wipe, but the combat reset between pulls covers that.
* Scenarios start mid-fight, so layouts timed from the start of combat run from the start of
  the scenario.

### Cactbot (IINACT)

With [IINACT](https://github.com/marzent/IINACT) loaded, AnoMech also writes the sim's fight into
IINACT's log stream, so cactbot's raidboss triggers and callouts run on it. Toggle **Send the sim
to IINACT (cactbot)** under **Plugin compatibility** (on by default).

* **The fight.** What a real pull's packets would have logged: casts and where they land, every
  hit (AoEs that hit nobody included), cast cancels, deaths, statuses gained and lost, head
  markers, tethers (and the tether an NPC spawns with), NPC teleports, targetability, model and
  animation changes, arena map effects and the director's updates, with a commence line at each
  pull and a wipe line when it ends.
* **Phase.** Cactbot's trigger files learn the phase, and some bosses' ids, from the cast that
  opens it, which a scenario starting mid-fight never plays. Each run first replays what the file
  needs for the scenario's phase (TOP, FRU, Dancing Mad, UWU), with no source name where a callout
  would react to it.
* **Zone.** Cactbot learns it from the log, so AnoMech writes the sim's zone on entry and the inn
  on leave.
* **Combat, party, combatants.** IINACT reads these from game memory, which already shows the
  sim: the in-combat flag above, the bots in the party list, the bosses in the object table.
* **Disk.** Nothing reaches IINACT's log file while you're in the sim (IINACT's own switch for
  PvP), so sim pulls never end up in a log you upload.
* **Pull start.** Cactbot starts a pull afresh, dropping what it gathered, when it sees combat
  begin, which can take up to 0.3 s. Each run's lines are held until then, so a run's first
  moments reach cactbot a fraction of a second late rather than not at all.
* **TOP.** Cactbot's TOP triggers offset every head marker by the pull's first one, so each TOP
  run opens with that marker. Tanks hear "Tank Cleaves" once at the start because of it.

Known gaps:
* Cactbot timelines count from the pull; one started mid-fight lines up only where the timeline
  allows a jump. Triggers don't depend on it.
* A callout that counts earlier mechanics of its phase stays quiet in a scenario starting after
  them: Stomp-a-Mole's Blizzard III puddle call counts Black Hole's lasers, for one.
* Only IINACT: plain ACT reads the game's network traffic, which the sim never goes through.
* IINACT has no API for this, so AnoMech reaches into its internals; an IINACT update could move
  them. The settings show what happened (`IINACT: ...`).

## Installation

See: https://github.com/anomek/MyDalamudPlugins

## Beta: Advanced Simulation Resolution

The simulator now includes a beta feature that properly resolves most skills, triggers, and gauges during simulation.
As this feature is still in beta, some edge cases and less common interactions may not yet resolve correctly.


## Currently implemented 
All using default NA pf strats unless noted otherwise:
- Dancing Mad (Ultimate)
    - P1 Tele-trouncing _by [Evernow](https://github.com/Evernow)_
    - P2 Forsaken
      - NA
        - [Kroxy-Rinon 341 (Center/N Stacks) melee adjust](https://raidplan.io/plan/UATE__aDcw1-bgVv)
        - [South Adjust 341](https://raidplan.io/plan/uq7zdjvuu7uuw8fj)
        - diamond markers or week one positions
      - EU _by [Wydox](https://github.com/Wydox)_
        - [\[LPDU\] Buddies](https://raidplan.io/plan/142oXOZpPc_jh3dd)
        - [\[Old\] p3Z Buddy Meow](https://raidplan.io/plan/lZWqxfxvyhF9sp3Z)
        - [\[Old\] zP6 South adjust](https://raidplan.io/plan/rtc1FcuZFMuyBzP6)
      - JP _by [altair9x](https://github.com/altair9x)_
        - [Yan](https://yan-flash.com/ultimate/yosei-ranbu#ult-phase-2)
    - P3 Limit Cut _by [Evernow](https://github.com/Evernow)_
    - P3 Black Hole _old bh (single tethers, n/s stomps)_
      - D>S>A
      - S>D>A _by [Evernow](https://github.com/Evernow)_
    - P3 Stomp-a-Mole _Black Hole from the end of its last wave, same strats_
    - P4 Kefka Says
      - kefkabin
      - kefkabin, gazes: supports N / DPS S _by [Evernow](https://github.com/Evernow)_
    - P5 Flood _by [Evernow](https://github.com/Evernow)_
    - P5 Exaflares _by [Wydox](https://github.com/Wydox)_
    - P5 Celestriad _by [RoarkGit](https://github.com/RoarkGit)_
    - P5 Forsaken Null _no ai or damage_
- Futures Rewritten (Ultimate) _by [grayparser](https://github.com/grayparser)_
    - P2 Diamond Dust 
    - P2 Light Rampant
    - P3 Ultimate Relativity
    - P3 Apocalypse 
    - P4 Darklit Dragonsong
      - NA
      - NA, you take Somber Dance
    - P4 Crystallize Time 
    - P5 Fulgent Blade
    - P5 Paradise Regained 
- The Omega Protocol (Ultimate)
    - P2 Party Synergy
      - NA pf
      - LPDU _by [Quantumlyy](https://github.com/Quantumlyy)_
    - P5 Delta
    - P5 Sigma
    - P5 Omega
    - P6 Exasquares / Wave Cannon 2
- The Weapon's Refrain (Ultimate) _by [WorstAquaPlayer](https://github.com/WorstAquaPlayer)_
    - Ultimate Predaction
    - Ultimate Suppression
- The Unending Coil of Bahamut (Ultimate) _by [RoarkGit](https://github.com/RoarkGit)_
    - Exaflares

## Details

* Spawns fake party members and boss NPCs into the live game client
* Drives their positions, cast bars, tethers, and VFX so mechanics play out visually
* Your fake party members are full fledged bots that will do mechanics.  
  Some scanarios also have solo mode where you can practice without disctractions.


## How to help
1. Please provide feedback and report any issues in scenarios: bad timing, damage, config not working at it supposed
2. Bot AI currently only covers strategies from my region. Adding strategies for other regions requires little coding.
   Feel free to create pull request or contact me.
3. Adding new scenario is more involved. `tools/parser.py` generates a baseline scenario from a log, which still
   needs randomization, mechanic-failure logic and bot AI added by hand.
4. Plugin-development or reverse-engineering help, and improvement ideas, are also welcome.


## Known issues
* Visuals should closely match retail, but damage snapshot timings are best guesses
* In scenarios for Top Omega Protocol (Ultimate):
  * Tether distance threshold are very rough estimations
* Not all skills will resolve properly

#  Acknowledgments

Thanks for contributors:
* [WorstAquaPlayer](https://github.com/WorstAquaPlayer) - rewriting core & fixing crashes, extra scenarios
* [Wydox](https://github.com/Wydox) - core improvements, extra scenarios and strategies
* [Evernow](https://github.com/Evernow) - multiplayer (WIP), core improvements, extra scenarions and strategies
* [RoarkGit](https://github.com/RoarkGit) - win streaks, extra scenarios
* [grayparser](https://github.com/grayparser) - core improvements, extra scenarios
* [altair9x](https://github.com/altair9x) - extra strategies
* [Quantumlyy](https://github.com/Quantumlyy) - extra strategies
* [Linqx](https://github.com/Linqx) - success/failure checkmarks

AnoMech leans heavily on the work of other Dalamud plugins. Huge thanks to their authors!  
Without them, the following would not be possible:

AnoMech's zone changes are built on **Hyperborea**, which served as the baseline for loading a solo duty arena.                                                                                                                                                                                   
[Evernow](https://github.com/Evernow) has since improved that code and made it safer to use.                                                                                                                                                                                                      

**FFXIV-RaidsRewritten** and **bossmod** were the initial inspiration for the project, though neither is part of the                                                                                                                                                                              
codebase anymore.

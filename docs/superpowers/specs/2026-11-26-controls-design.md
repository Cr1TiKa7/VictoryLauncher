# Entwurf: Tastenbelegung im Launcher

Stand: 2026-11-26, vom Nutzer so freigegeben.

## Ziel

Der Nutzer sieht im Launcher, welche Taste für welche Controller-Eingabe steht, passend zum gewählten Fork und zu
seiner Tastatur.

Nicht Ziel: Tasten umbelegen, `VICTORY_*`-Variablen für die Eingabe setzen.

## Ansätze

1. **Feste Tabelle im Launcher (gewählt).** Einfach und ohne Checkout sichtbar. Ändert ein Fork seine Tasten, stimmt
   die Anzeige nicht mehr.
2. **Aus `gameinput.rs` des Checkouts lesen.** Folgt Updates, braucht aber einen Checkout und bricht leicht bei
   Codeänderungen.

## Anzeige

Ein Knopf `CONTROLS` neben `START GAME` öffnet ein Fenster mit zwei Spalten (Controller, Tastatur) und Hinweisen
darunter. Er ist immer bedienbar, auch während Warmup und Spiel, nur nicht beim Eintragen einer eigenen Git-URL.

## Belegung (Quelltext, Stand 2026-11-26)

- Standard (alle Forks außer AnyXboxX): feste virtuelle Tasten, also die Taste, auf der der Buchstabe steht.
  kelvaroq wurde an seinem Basisstand f85101b geprüft.
- AnyXboxX: Buchstaben zählen nach ihrer Position auf der Tastatur (`keymap.rs`, Standard ohne `VICTORY_KEYBOARD`).
  Der Launcher fragt Windows mit demselben Aufruf wie die Runtime, welche Taste an der Position liegt. Zusätzlich
  Shift = L3, Strg = R3 und die Maus. Der Launcher gibt `VICTORY_KEYBOARD` nicht weiter, daher gilt dieser Standard.
- Eigene Git-URL: Standardtabelle mit dem Hinweis, dass sie für diesen Fork nicht geprüft ist.
- Hinweise: Tasten zählen nur, wenn das Spielfenster den Fokus hat und kein Controller verbunden ist (DexNom: auch
  kein DualSense am Kabel). Vice-Tay: F11 oder Alt+Enter wechselt den Vollbildmodus.

## Dem Umsetzer überlassen

Aussehen des Fensters im vorhandenen Thema, Namen der Typen.

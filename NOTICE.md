# Attribution and prior work

Claudelk is licensed under the MIT License (see `LICENSE`). It builds on
ideas and reverse-engineered protocol details from the following projects.
None of their source code is copied verbatim; the byte-level command
formats below are facts about a hardware protocol and are credited here in
recognition of the upstream work that documented them.

## Claude Code integration design

- **claude-lamp** by **bobek-balinek** — MIT
  https://github.com/bobek-balinek/claude-lamp

  The hooks → daemon → BLE architecture (and the idea of a persistent
  connection driven by Claude Code lifecycle events) was inspired by this
  project. Claudelk targets a different lamp family (ELK-BLEDOM) and is
  written in C# rather than Python, but the overall shape of the
  integration follows claude-lamp's playbook.

## ELK-BLEDOM protocol references

- **b1scoito/elk-led-controller** — MIT
  https://github.com/b1scoito/elk-led-controller

  Primary reference for the 9-byte command format used in
  `Claudelk.Core.Protocol.ElkBledomProtocol`.

- **TheSylex** — original reverse-engineering of the ELK-BLEDOM protocol.

- **arduino12/ble_rgb_led_strip_controller** — GPL-3.0
  https://github.com/arduino12/ble_rgb_led_strip_controller

  Consulted as a secondary cross-check on packet structure. No source
  code is incorporated; only the documented protocol facts are used.

## Runtime dependencies

- **32feet.NET / InTheHand.BluetoothLE** — MIT
  https://github.com/inthehand/32feet

If you believe an attribution is missing or incorrect, please open an
issue.

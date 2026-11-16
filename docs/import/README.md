# VPS-00A — Source Import Manifest

Status: IN PROGRESS

- Source snapshot: `chung6a8m/MyDmsVn` at `bac73004328884f5322c7c87d9787b8150885ac0`
- WinFormsKit snapshot: `chung6a8m/MyDmsVn.WinFormsKit` at `433e5b53f979e37cc6b69bad6dfcde0975c7ecaf`
- Import method: independent copy; no fork, no source Git history and no upstream remote.
- Imported business behavior must remain unchanged in VPS-00A.
- Desktop production target is `net48`; `Desktop.AppCore` must be excluded.
- Legacy migration SQL must be retained; modernization belongs to later VPS increments.
- Existing test success/production acceptance from the source is not inherited.
- VPS-00B will create the complete independent documentation, decisions, roadmap and CI.

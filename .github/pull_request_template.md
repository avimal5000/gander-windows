## What & why

Closes #

## Behavior checklist
- [ ] Cursor is never moved; only keyboard focus changes
- [ ] Uncertain input (ambiguous pose, face lost, camera busy, Focus Lock on) results in no switch
- [ ] Works with the current monitor layout; layout changes handled

## Privacy checklist
- [ ] No networking code (`scripts/privacy-guard.sh` passes)
- [ ] No frames or images written anywhere; only numeric pose traces
- [ ] Camera runs only while needed (pause, sleep, lock, quit turn it off)
- [ ] No synthesized or captured input
- [ ] No analytics, accounts, or crash reporters

## Verification
- [ ] `dotnet build` has no warnings and `dotnet test` passes
- [ ] `dotnet format --verify-no-changes` is clean
- [ ] Tried on the Windows desktop (describe), or not needed because the change is portable

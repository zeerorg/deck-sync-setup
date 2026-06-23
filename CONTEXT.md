# Deck Sync Setup

This context defines the core language for installing and removing the deck sync runtime on a host machine.

## Language

**Deck sync runtime**:
The local runtime files used by deck-sync-setup to enable sync setup on a machine.
_Avoid_: install output, temp files, artifacts

**Setup Install module**:
The module that prepares the deck sync runtime by selecting and placing the correct package for the current host.
_Avoid_: installer service, install handler, install pipeline

**Setup Cleanup module**:
The module that removes deck sync runtime files from the host.
_Avoid_: cleanup service, cleanup handler, delete command

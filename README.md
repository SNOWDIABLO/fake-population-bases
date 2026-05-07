# FakePopulationBases

Plugin Oxide / uMod pour serveur **Rust** — spawne des fausses bases "honeypot" via les templates CopyPaste pour simuler une population active sur le serveur.

> Idéal en début de wipe ou en heures creuses pour rendre la map plus vivante.

---

## Fonctionnalités

- Spawn automatique de bases via templates `CopyPaste`
- Permission admin (`fakepopulationbases.admin`) pour la gestion
- Configuration par fichier JSON (zones de spawn, fréquence, templates utilisés)
- Détection auto de proximité joueurs (les bases sont "vivantes" à distance, inertes près des joueurs pour éviter la triche)

---

## Stack

- **Langage** : C# (`.cs` Oxide plugin)
- **Plateforme** : Rust dedicated server
- **Frameworks** : Oxide / uMod 2.0+, dépendance optionnelle `CopyPaste` plugin

---

## Installation

1. Copier `FakePopulationBases.cs` dans `oxide/plugins/` de ton serveur Rust
2. Le plugin s'auto-compile au prochain reload
3. (Optionnel) Installer le plugin [CopyPaste](https://umod.org/plugins/copy-paste) si pas déjà présent
4. Configurer dans `oxide/config/FakePopulationBases.json` (généré au premier lancement)

---

## Commandes admin

| Commande | Effet |
|---|---|
| `/fpb spawn` | Force le spawn d'une base |
| `/fpb clear` | Supprime toutes les bases gérées par le plugin |
| `/fpb stats` | Affiche le nombre de bases actives |

(Permission requise : `fakepopulationbases.admin`)

---

*Plugin développé en collaboration avec Claude Code (Anthropic).*

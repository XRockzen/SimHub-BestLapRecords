# Best Lap Records — plugin SimHub pour Le Mans Ultimate

Carnet de bord d'endurance pour **Le Mans Ultimate** : carrière, records, historique de
toutes les sessions et analyse tour par tour, avec **comparaison côte à côte entre
coéquipiers** (jusqu'à 4 pilotes).

## Fonctionnalités

- **Carrière** : courses, victoires, podiums, top 5, abandons, tours et temps de roulage ;
  meilleur temps par circuit × catégorie ; activité semaine par semaine.
- **Historique** : toutes les sessions (essais, qualifs, courses), y compris les serveurs privés,
  avec filtres par type de session.
- **Progression** : par circuit et catégorie, l'évolution du meilleur tour session après session.
- **Analyse d'une session** : chaque tour avec secteurs, carburant, énergie virtuelle, usure des
  pneus et arrêts aux stands ; statistiques recalculées sur les tours que vous cochez.
- **Capture en direct** pendant que vous roulez (SimHub ouvert) : litres consommés par tour,
  usure par tour, temps dans la voie des stands, temps à l'arrêt, carburant ajouté.
- **Équipe** : chaque pilote partage ses données via un dossier Google Drive commun ;
  les sessions courues ensemble sont détectées et surlignées, et s'analysent côte à côte.
- **Mises à jour** : un bandeau apparaît dans le plugin quand une nouvelle version est publiée.

## Installation (première fois)

1. Téléchargez **`User.BestLapRecords.dll`** depuis la
   [dernière release](../../releases/latest).
2. **Clic droit sur le fichier → Propriétés → cochez « Débloquer » → OK.**
   Sans cela, Windows peut empêcher SimHub de charger un fichier venu d'Internet.
3. Fermez SimHub, puis copiez la DLL dans le dossier d'installation de SimHub
   (par défaut `C:\Program Files (x86)\SimHub\`).
4. Lancez SimHub et acceptez l'activation du nouveau plugin. Il apparaît dans le menu
   sous **« Best Laps »**.
5. En haut du plugin, renseignez votre **nom de joueur LMU** (exactement comme dans le jeu),
   vérifiez le **dossier résultats** de LMU (détecté automatiquement le plus souvent),
   puis cliquez sur **⟳ Importer**.

Ensuite, c'est automatique : chaque session terminée dans LMU est importée toute seule.

## Mises à jour

Au démarrage de SimHub, le plugin vérifie s'il existe une version plus récente.
Si oui, un bandeau bleu affiche les nouveautés avec un bouton **Installer** :

1. cliquez sur **Installer** — le fichier est téléchargé depuis ce dépôt et vérifié
   (empreinte SHA-256, nom et version) avant d'être mis en place ;
2. **redémarrez SimHub** pour activer la nouvelle version.

Si SimHub est installé dans un dossier protégé (`C:\Program Files…`), Windows peut demander
une autorisation administrateur pendant l'installation.
Le lien **« Vérifier les mises à jour »** en bas à droite du plugin relance la recherche.

## Partage avec l'équipe (Google Drive)

1. Installez [Google Drive pour ordinateur](https://www.google.com/drive/download/).
2. Le capitaine crée un dossier (par ex. `LMU Équipe`) et le partage avec les coéquipiers en
   mode éditeur. Chaque coéquipier l'ajoute à son Drive : site Drive → *Partagés avec moi* →
   clic droit sur le dossier → **Ajouter un raccourci dans Drive**.
3. Dans le plugin : **📁 Choisir…** → sélectionnez ce dossier dans le lecteur Google Drive.

Vos données sont publiées automatiquement (démarrage de SimHub, fin de session, fermeture).
**⇅ Synchroniser** force l'échange. Vos coéquipiers apparaissent ensuite dans
**« + Ajouter un pilote… »**. Leurs données restent séparées des vôtres et ne sont jamais modifiées.

## Pour le développeur

Prérequis : Windows, SimHub installé, .NET Framework 4.8,
[GitHub CLI](https://cli.github.com/) connecté (`gh auth login`).

```powershell
# Compiler et installer dans SimHub (SimHub fermé)
$env:SIMHUB_INSTALL_PATH = "F:\Program Files (x86)\SimHub\"
& "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe" User.BestLapRecords.csproj /p:Configuration=Release

# Publier une nouvelle version (bandeau de mise à jour chez tous les pilotes)
.\release.ps1 -Version 1.1.0 -Notes "- Nouveauté 1`n- Correction 2" -Deploy
```

Le projet référence les DLL de SimHub via `SIMHUB_INSTALL_PATH` : elles ne sont pas incluses
dans ce dépôt. Les données des pilotes (`PluginsData\Common\BestLapRecords\*.db`) ne sont jamais
publiées ici.

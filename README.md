# TeamPace

**Comparez votre rythme à celui de vos coéquipiers sur Le Mans Ultimate.**

TeamPace est un plugin [SimHub](https://www.simhubdash.com/) fait pour un petit groupe de pilotes
qui se connaissent : votre équipe, vos amis. Chacun garde ses propres données, les partage via un
simple dossier Google Drive commun, et tout le monde peut se comparer côte à côte : carrière,
records, sessions courues ensemble, tour par tour. Les données servent aussi à préparer les
courses d'endurance : consommation, usure des pneus, temps perdu aux stands.

![Carrière de deux pilotes côte à côte](docs/images/01-carriere-comparaison.png)

- [Ce que fait TeamPace](#ce-que-fait-teampace)
- [Installation](#1-installation)
- [Premiers réglages](#2-premiers-réglages)
- [Partager avec ses coéquipiers (Google Drive)](#3-partager-avec-ses-coéquipiers-google-drive)
- [Se comparer](#4-se-comparer)
- [Mises à jour](#5-mises-à-jour)
- [Dépannage](#dépannage)
- [Historique des versions](CHANGELOG.md)

## Ce que fait TeamPace

| | |
|---|---|
| **Comparaison entre coéquipiers** | jusqu'à 4 pilotes côte à côte, une colonne et une couleur par pilote |
| **Sessions communes** | les courses et essais faits ensemble sont détectés et surlignés automatiquement |
| **Carrière** | courses, victoires, podiums, temps de roulage, record par circuit et par catégorie, activité semaine par semaine |
| **Progression** | courbe du meilleur tour de chaque pilote sur un circuit, session après session |
| **Analyse tour par tour** | secteurs, écart au coéquipier sur chaque tour, carburant, énergie virtuelle, usure des pneus, arrêts aux stands |
| **Capture en direct** | pendant que vous roulez (SimHub ouvert) : litres par tour, usure par tour, temps dans la voie des stands et à l'arrêt |
| **Automatique** | chaque session terminée dans LMU est importée seule, serveurs privés compris |

TeamPace ne publie rien sur Internet : vos données ne vont que dans le dossier Google Drive
que **vous** partagez avec les personnes de **votre** choix.

## 1. Installation

1. Téléchargez **`User.TeamPace.dll`** depuis la [dernière version](../../releases/latest).
2. **Clic droit sur le fichier → Propriétés → cochez « Débloquer » → OK.**
   Windows bloque par sécurité les fichiers venus d'Internet ; sans cette étape, SimHub peut
   refuser de charger le plugin.
3. **Fermez SimHub**, puis copiez la DLL dans le dossier d'installation de SimHub
   (par défaut `C:\Program Files (x86)\SimHub\`).
4. Lancez SimHub et acceptez l'activation du nouveau plugin.
   **TeamPace** apparaît dans le menu de gauche.

C'est la seule installation manuelle : les versions suivantes s'installent depuis le plugin
([Mises à jour](#5-mises-à-jour)).

## 2. Premiers réglages

![Premiers réglages](docs/images/02-premiers-reglages.png)

1. **⚙ Réglages** : ouvre ou replie ce panneau. Il est ouvert d'office au premier lancement, puis
   se replie pour laisser la place à vos données.
2. **Joueur LMU** : votre nom exactement comme dans Le Mans Ultimate.
3. **Dossier résultats** : le dossier des résultats LMU. Il est détecté automatiquement dans la
   plupart des cas (`…\Le Mans Ultimate\UserData\Log\Results`).
4. **⟳ Importer** : récupère toutes vos sessions passées. La première fois, comptez quelques secondes.

Ensuite, plus rien à faire : chaque session que vous terminez dans LMU est ajoutée toute seule.

## 3. Partager avec ses coéquipiers (Google Drive)

Le principe : un dossier Google Drive commun à l'équipe. Le plugin de chaque pilote y dépose
son fichier de données et y lit ceux des autres. Pas de serveur, pas de compte supplémentaire.

### 3.1 Installer Google Drive pour ordinateur (chaque pilote)

Téléchargez **Google Drive pour ordinateur** sur
[google.com/drive/download](https://www.google.com/drive/download/), installez-le et connectez-vous
avec votre compte Google. Votre Drive apparaît alors comme un **lecteur** dans l'Explorateur Windows.

![Google Drive devient un lecteur Windows](docs/images/drive-1-installer.svg)

### 3.2 Créer et partager le dossier d'équipe (le capitaine, une seule fois)

Sur [drive.google.com](https://drive.google.com) : **Nouveau → Nouveau dossier**, nommez-le par
exemple `LMU Équipe`, puis partagez-le avec chaque coéquipier **en tant qu'Éditeur**.

![Créer et partager le dossier d'équipe](docs/images/drive-2-partager.svg)

> « Éditeur » est indispensable : chaque pilote doit pouvoir déposer son propre fichier.
> Aide Google : [Partager des dossiers dans Google Drive](https://support.google.com/drive/answer/7166529).

### 3.3 Ajouter le dossier à son Drive (chaque coéquipier)

Un dossier partagé avec vous n'apparaît **pas** sur votre ordinateur tant que vous ne l'avez pas
ajouté à votre Drive. Sur [drive.google.com](https://drive.google.com) :
**Partagés avec moi → clic droit sur le dossier → Organiser → Ajouter un raccourci → Mon Drive**.

![Ajouter un raccourci vers le dossier partagé](docs/images/drive-3-raccourci.svg)

> Aide Google : [Raccourcis Google Drive](https://support.google.com/drive/answer/9700156).

### 3.4 Indiquer le dossier dans TeamPace (chaque pilote)

Dans TeamPace, cliquez sur **📁 Choisir…** et sélectionnez le dossier d'équipe dans le lecteur
Google Drive :

![Choisir le dossier d'équipe](docs/images/drive-4-choisir-dossier.svg)

La synchronisation démarre aussitôt. Le dossier choisi s'affiche dans les **⚙ Réglages** :

![Réglages d'équipe dans TeamPace](docs/images/04-dossier-equipe.png)

1. **Dossier équipe** : le dossier partagé sélectionné.
2. **📁 Choisir…** : pour le changer.
3. **⇅ Synchroniser** : force l'échange tout de suite. Sinon, c'est automatique (au démarrage de
   SimHub, à la fin de chaque session et à la fermeture).
4. **Pilotes affichés** : vous et les coéquipiers ajoutés (voir ci-dessous).

## 4. Se comparer

### Se déplacer dans TeamPace

Deux onglets en haut : **Carrière** (point de départ) et **Historique** (toutes les sessions).
Un double-clic descend d'un niveau : *circuit → progression → analyse d'une session*. Pour remonter,
le bouton **←**, le **fil d'Ariane** au-dessus du titre (chaque niveau est cliquable), le bouton
« précédent » de la souris ou **Alt + ←**.

### Ajouter un coéquipier à l'écran

![Ajouter un pilote](docs/images/05-ajouter-un-pilote.png)

**+ Ajouter un pilote…** liste les coéquipiers dont le fichier est arrivé dans le dossier d'équipe.
Chaque pilote ajouté ouvre une colonne, jusqu'à 4. Le **✕** sur son nom le retire. Leurs données
sont en lecture seule : vous ne pouvez rien modifier chez eux, et eux rien chez vous.

### Carrière

Une colonne par pilote : statistiques, record par circuit et catégorie, et activité semaine par
semaine (même échelle pour tous les pilotes). La colonne **Écart** indique qui détient le record (★)
et à combien sont les autres. Les filtres de catégorie s'appliquent à toutes les colonnes.

![Mise en évidence d'un circuit](docs/images/09-carriere-focus.png)

**Un clic sur un circuit le met en évidence chez tous les pilotes** : toutes les autres lignes sont
grisées, il ne reste que ce circuit dans cette catégorie (1, 2), et les graphiques d'activité ne
colorent plus que lui. Un coéquipier qui n'y a jamais roulé dans cette catégorie a toute sa colonne
grisée. Re-cliquez sur la ligne pour annuler. Un **double-clic** ouvre la **progression**.

### Progression sur un circuit

![Progression sur un circuit](docs/images/03-progression.png)

1. Le **fil d'Ariane** : cliquez « Carrière » pour remonter directement.
2. La **courbe** : chaque point est le meilleur tour d'une session, une couleur par pilote, et la
   ligne suit le record de chacun. **Plus c'est bas, plus c'est rapide** : une courbe qui descend,
   c'est un pilote qui progresse (★ = record). Les sessions sont espacées régulièrement, dans l'ordre
   chronologique ; les points cerclés d'orange sont des sessions communes. Un clic sélectionne la
   session dans les tableaux ; un double-clic l'analyse.
3. Les **chiffres clés** de chaque pilote : record, écart au meilleur de l'équipe, progression depuis
   la première session, dernière séance et temps roulé sur ce circuit.
4. **←** : retour à l'écran précédent.

### Sessions communes

![Historique et sessions communes](docs/images/06-historique-sessions-communes.png)

Les filtres en haut (**Courses**, **Qualifs**, **Essais**, **Sim complète**) s'appliquent à tous les
pilotes.

1. **⇄ Communes uniquement** : n'affiche que les sessions faites ensemble.
2. Les **pastilles de couleur** indiquent avec quels pilotes la session a été courue.
3. Les **lignes ambrées** sont les sessions communes. Un clic sélectionne la même session dans toutes
   les colonnes ; un **double-clic ouvre l'analyse côte à côte**.

### Analyse côte à côte

![Analyse d'une course commune](docs/images/07-analyse-cote-a-cote.png)

Chaque colonne se lit de haut en bas :

- la **carte de session** : voiture, équipe, et les chiffres clés (arrivée, départ, meilleur tour,
  théorique, vitesse max, tours, arrêts, incidents, tours invalides). Au survol du **temps
  théorique** : les trois meilleurs secteurs et le tour où chacun a été réalisé. Au survol de
  **Invalides** : le détail des limites de piste, des contacts et des pénalités de la session ;
- l'**analyse des tours cochés** : *Rythme* (moyenne), *Régularité* (écart-type : plus il est bas,
  plus les tours se ressemblent), carburant, énergie virtuelle, usure des pneus et arrêts aux stands
  avec le temps perdu ;
- le **tableau tour par tour**, avec sa sélection juste au-dessus et sa légende juste en dessous.

1. **Δ pilotes** : sur chaque tour, l'écart au plus rapide de l'équipe (★ = le plus rapide).
2. L'écart de votre **moyenne** à la meilleure moyenne de l'équipe.
3. **Rythme** coche les tours représentatifs (hors tour 1, passages aux stands et tour de sortie).
   Cochez ou décochez des tours : statistiques et couleurs se recalculent.

Les couleurs : vert = meilleur tour, rouge = pire tour, violet = meilleur secteur.
La colonne **Info** reprend les décisions de la direction de course enregistrées par LMU, tour par
tour (détail au survol) :

| Repère | Signification |
|---|---|
| **PIT** | tour où vous êtes entré dans la voie des stands |
| **SORTIE** | tour de sortie du garage ou des stands |
| **PNEU** | changement de gomme |
| **INV** | tour **invalidé** par une coupure ou une sortie de piste |
| **AVERT** | avertissement pour limites de piste |
| **LIM** | dépassement des limites de piste, sans conséquence |
| **CHOC** | contact (mur, autre voiture) |
| **PÉN** | pénalité reçue pendant ce tour |

Les tours de stand et de sortie ne sont **pas** comptés comme invalides : le compteur **Invalides**
ne compte que les tours invalidés pour limites de piste. LMU ne donne pas de chrono pour ces tours
sans temps officiel : TeamPace le reconstitue à partir de l'heure de départ du tour suivant et
l'affiche **entre parenthèses**, sans jamais le compter dans les statistiques. Les colonnes des pilotes gardent la même structure, pour que les tableaux restent
alignés tour par tour.

## 5. Mises à jour

![Bandeau de mise à jour](docs/images/08-mise-a-jour.png)

Au démarrage de SimHub, TeamPace vérifie s'il existe une nouvelle version. Si oui, un bandeau
affiche les nouveautés :

1. **Installer** : le fichier est téléchargé depuis ce dépôt et vérifié (empreinte SHA-256, nom et
   version) avant d'être mis en place. **Redémarrez SimHub** pour activer la nouvelle version.

Si SimHub est installé dans `C:\Program Files…`, Windows peut demander une autorisation
administrateur. Le lien **Vérifier les mises à jour**, en bas à droite du plugin, relance la recherche.

## Dépannage

| Problème | Solution |
|---|---|
| TeamPace n'apparaît pas dans SimHub | Vérifiez l'étape « Débloquer » de l'installation, puis relancez SimHub. |
| Aucune session après l'import | Le **nom de joueur** doit être exactement celui de LMU ; vérifiez le **dossier résultats**. |
| Un coéquipier n'apparaît pas dans « + Ajouter un pilote… » | Il doit avoir choisi le même dossier d'équipe et lancé SimHub au moins une fois. Cliquez ensuite sur **⇅ Synchroniser**. |
| Le dossier d'équipe est introuvable dans « Choisir… » | Le coéquipier doit d'abord l'**ajouter à son Drive** (étape 3.3). |
| Colonnes carburant / pneus vides | Ces valeurs sont capturées en direct : roulez avec SimHub ouvert. En ligne, LMU ne les écrit pas dans ses fichiers de résultats. |

## Pour le développeur

Prérequis : Windows, SimHub installé, .NET Framework 4.8,
[GitHub CLI](https://cli.github.com/) connecté (`gh auth login`).

```powershell
# Compiler et installer dans SimHub (SimHub fermé)
$env:SIMHUB_INSTALL_PATH = "F:\Program Files (x86)\SimHub\"
& "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe" User.TeamPace.csproj /p:Configuration=Release

# Publier une nouvelle version (bandeau de mise à jour chez tous les pilotes)
.\release.ps1 -Version 2.4.0 -NotesFile notes.md -Deploy
```

Le projet référence les DLL de SimHub via `SIMHUB_INSTALL_PATH` : elles ne sont pas incluses dans ce
dépôt. Les données des pilotes (`PluginsData\Common\TeamPace\`) ne sont jamais publiées ici.
Les captures de cette page utilisent des données de démonstration anonymisées.

# Historique des versions

Chaque version est publiée dans les [releases](../../releases) et proposée automatiquement dans
TeamPace par le bandeau de mise à jour.

## 2.4.0

- **Correction** : les données capturées en direct (carburant, usure des pneus, arrêts aux stands)
  pouvaient ne pas s'afficher dans l'analyse alors qu'elles étaient bien enregistrées. LMU écrit une
  heure de début de session décalée de quelques minutes, différente pour chaque pilote ; le
  rattachement se fait désormais par numéro de tour. Les sessions déjà roulées sont corrigées
  automatiquement.
- Temps théorique détaillé : les trois meilleurs secteurs et le tour d'où vient chacun.
- Tours invalides : chrono reconstitué (entre parenthèses, hors statistiques) et compteur
  « Invalides » dans la carte de session.

## 2.3.0

- Progression : la courbe se lit maintenant de façon naturelle, plus bas = plus rapide.
  Une courbe qui descend, c'est un pilote qui progresse.
- Carrière : un clic sur un circuit le met en évidence chez tous les pilotes et grise le reste
  (re-clic pour annuler). Les graphiques d'activité suivent.
- Documentation mise à jour.

## 2.2.0

- Navigation repensée : barre unique avec onglets, pilotes et bouton Réglages repliable.
- Titre de page et fil d'Ariane cliquable ; retour avec le bouton « précédent » de la souris ou Alt + ←.
- Progression d'un circuit : courbe commune à tous les pilotes et chiffres clés (record, écart
  équipe, progression, dernière séance, temps roulé).

## 2.1.0

- Analyse de session plus lisible : carte de session avec chiffres clés, blocs d'analyse alignés
  entre pilotes, sélection des tours au-dessus du tableau et légende en dessous.
- Nouveau : régularité (écart-type des tours sélectionnés).

## 2.0.0

- Best Lap Records devient **TeamPace**. Les données existantes sont reprises automatiquement.
- Guide d'installation illustré, avec le partage Google Drive pas à pas.

## 1.0.0

- Première version publique (sous le nom Best Lap Records) : carrière, historique, analyse tour par
  tour, capture en direct, comparaison jusqu'à 4 pilotes, partage d'équipe par Google Drive et
  mises à jour automatiques.

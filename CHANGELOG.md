# Historique des versions

Chaque version est publiée dans les [releases](../../releases) et proposée automatiquement dans
TeamPace par le bandeau de mise à jour.

## 3.2.0

- **Stratégie** : carburant au choix à chaque arrêt (auto, plein ou litres tapés à la main). Le temps
  d'arrêt et la longueur du relais suivant suivent ; un plein permet de pousser un relais complet,
  carburant et pneus.
- Documentation et capture de la stratégie mises à jour.

## 3.1.0

- **Correction importante** : quand Google Drive change de lettre de lecteur (J: → I: après un
  redémarrage), TeamPace ne partageait plus rien sans prévenir (base, télémétrie). Il retrouve
  maintenant le dossier d'équipe sur sa nouvelle lettre, et affiche un ⚠ s'il reste introuvable.
- **Stratégie** : nombre de pneus changés au choix à chaque arrêt (auto, aucun, 1 à 4), avec le temps
  d'arrêt qui suit ; temps de changement de 2 pneus mesuré ou réglable.
- **Stratégie** : les plans enregistrés sont partagés avec l'équipe par le dossier d'équipe
  (« Plans de l'équipe », avec l'auteur et la date) ; les pilotes d'un plan sont repérés par leur nom,
  pour qu'un plan s'ouvre chez chacun.
- Les pop-ups de notification ne cachent plus les boutons de l'en-tête.

## 3.0.0

- **Nouvelle navigation** : trois onglets (Carrière, Historique, Stratégie) et une **page circuit**
  avec trois onglets (Progression, Télémétrie, Plan de travail) ; deux niveaux au plus. Un menu
  change de circuit sans revenir en arrière.
- **Carrière face à face** : une ligne par circuit et catégorie, le record de chaque pilote sur la
  même ligne dans un bloc à sa couleur, qui mène et de combien ; chiffres clés comparés ;
  « records détenus ».
- **Activité** : la légende disparaît ; un clic sur une barre sélectionne la ligne du circuit, un
  clic sur une ligne fait ressortir ses barres chez chaque pilote.
- **Historique** : une ligne par session, groupée par jour, le résultat de chaque pilote sur la même
  ligne ; filtres par catégorie et par circuit ; sessions vides masquées.
- **Plan de travail** (nouveau) : les trois virages où gagner du temps, face à ton meilleur passage,
  un coéquipier ou le meilleur de l'équipe ; la phase en cause (freinage, corde, sortie) et la mesure
  qui l'explique ; ton tour idéal.
- **Stratégie** (nouveau) : planificateur d'endurance. Rythme, dégradation, consommation, énergie
  virtuelle, usure et temps d'arrêt mesurés sur vos sessions (modifiables) ; relais équilibrés,
  arrêts détaillés, choix du pilote de chaque relais, plans enregistrés.
- **Notifications** (nouveau) : record battu ou amélioré, victoire, podium, session commune,
  télémétrie reçue ; pop-up à l'arrivée et cloche avec le nombre de nouveautés non lues.
- **Télémétrie** : force G, ABS et antipatinage ajoutés à l'extraction (relue automatiquement au
  premier « ⟳ Tout mettre à jour ») ; noms des virages au-dessus des courbes.
- Les lignes d'aide permanentes sont remplacées par des info-bulles et un bouton **?** qui déplie
  l'aide de chaque écran (avec la légende de l'analyse).
- Textes agrandis d'un cran, boutons harmonisés, temps de secteur au même format que le reste
  (« 28.569 »).
- Documentation et captures mises à jour.

## 2.7.1

- Le tracé des circuits (bords de piste, virages) utilise les tours de toute l'équipe, même des
  pilotes non affichés : un circuit roulé par un seul coéquipier a quand même son tracé.
- La courbe « Trajectoire » est retirée : la carte montre les trajectoires plus clairement.
- Les commandes de la carte (« ⤢ Agrandir », clic, molette) sont regroupées en haut de la carte.

## 2.7.0

- **Trajectoires** : la carte suit le zoom des courbes et montre la piste avec ses bords, la ligne
  de chaque tour, le début de freinage et la remise des gaz de chaque pilote. Précision au
  centimètre grâce au placement latéral enregistré par LMU.
- **Carte agrandie** : bouton « ⤢ Agrandir la carte » pour comparer les trajectoires en grand,
  les courbes principales restant affichées dessous.
- Nouvelle courbe **Trajectoire** : placement sur la largeur de la piste, mètre par mètre.
- **Virages** détectés automatiquement, avec le temps gagné ou perdu sur la référence dans chacun ;
  un clic zoome dessus (sur les courbes et la carte).
- Clic et molette sur la carte pour zoomer, survol de la carte pour lire les valeurs.
- La télémétrie déjà extraite est relue automatiquement au premier « ⟳ Tout mettre à jour » pour
  y ajouter le placement sur la piste.

## 2.6.0

- **Télémétrie** : superposez jusqu'à 4 tours (vous, vos coéquipiers, n'importe quelle séance sur le
  circuit) : écart cumulé, vitesse, accélérateur, frein, rapport, volant et régime, avec le tracé du
  circuit. Zoom à la molette, valeurs au survol. TeamPace reprend la télémétrie que LMU enregistre
  déjà, historique compris, et la partage avec l'équipe par le dossier Google Drive.
- Un seul bouton **⟳ Tout mettre à jour** : import des résultats, télémétrie et synchronisation de
  l'équipe.
- Arrêts aux stands : un retour au garage (Échap) suivi d'une sortie n'est plus pris pour un arrêt.
  Le vrai arrêt du même tour s'affiche avec son temps et son carburant.
- Temps perdu aux stands : un tour de sortie écourté (retour au garage) n'est plus compté.
- Nombre de pneus changés (2 ou 4) détecté pendant l'arrêt.
- Documentation : section Télémétrie et captures d'écran mises à jour (bouton « Tout mettre à jour »).

## 2.5.0

- Tours invalides : seuls les tours invalidés par une coupure ou une sortie de piste sont comptés.
  Les tours de stand et de sortie du garage ne le sont plus.
- Nouveaux repères tour par tour, tirés du journal de LMU : SORTIE, AVERT (avertissement limites),
  LIM (limites sans conséquence), CHOC (contact), PÉN (pénalité), avec le détail au survol.
- Temps théorique : les meilleurs secteurs s'affichent au survol (une ligne gagnée).
- Arrêts aux stands : bloc sur deux largeurs, sans barre de défilement ; temps perdu calculé aussi
  pour les tours de stand sans chrono officiel.
- Correction : le tour qui commence dans les stands (sortie) n'est plus pris pour un passage au stand.
- Les fichiers LMU sont relus automatiquement au premier démarrage pour extraire ces informations.

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

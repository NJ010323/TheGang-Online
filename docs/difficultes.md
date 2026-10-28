# Difficultés et cartes spéciales

Référence : livret français TheGang_Manual_FR_V1.pdf fourni par Nolan, pages 14 à 19.

## Choisir la difficulté

Dans le salon, ouvrir **Difficulté · Cartes spéciales**. Seul l’hôte choisit le mode,
mais tous peuvent lire les explications et consulter le catalogue de vingt cartes.
Le choix est partagé aux invités, conservé lors d’une revanche et transféré lors
d’une relève. Le menu des tutoriels propose une initiation Standard et des
parcours Avancé, Pro et Gangster, ainsi que des ateliers pour chaque carte.

| Mode | Départ | Après un braquage | Défaite |
| --- | --- | --- | --- |
| Standard | Aucune carte spéciale | Aucune | 3 alarmes |
| Avancé | Premier braquage normal | Une Effraction après succès, un Complice après échec, pour le prochain braquage seulement | 3 alarmes |
| Pro | Une Effraction aléatoire permanente, sans Ouverture facile | Comme Avancé, en plus de la permanente | 3 alarmes |
| Gangster | Deux Effractions aléatoires, sans Ouverture facile | Remplacer celle au plus petit numéro ; aucun Complice | 2 alarmes |

Les piles de découverte Avancé commencent dans l’ordre 1 à 10 et tournent entre
les braquages et les revanches d’un même salon. En Pro, la permanente est retirée
de la pile temporaire ; en Gangster, les deux cartes actives ne sont jamais en
même temps dans la pioche. Comme indiqué par le livret, le nombre de joueurs
influe aussi sur la difficulté.

## Lire et utiliser les cartes

Le plateau affiche les cartes actives dans la colonne des jetons. Cliquer une
carte pour l’agrandir : son nom, son moment d’application et son effet sont repris
en grand texte. Le bouton **Cartes & effets**, en haut du plateau, reste accessible
même si le tableau latéral est masqué. Le catalogue permet aussi de consulter les
cartes inactives. Échap ferme la vue sans interrompre la partie.

Les effets automatiques sont appliqués par le moteur : jetons sombres non
transférables, tours sautés, remplacement conditionnel des mains au flop,
disparition de l’historique, troisième carte, annonces du nombre de têtes ou de
la somme des cartes. Une notification signale les nouvelles cartes actives.

Les Complices facultatifs sont proposés dans Cartes & effets. Le bénéficiaire,
le destinataire ou le rang choisi sont soumis à un vote de toute la table.
Un refus annule le vote sans consommer la carte. Avant de proposer après une
confirmation générale, utiliser **Retirer** sur la roue. Le pouvoir n’est utilisable
qu’une fois et expire à la fin du braquage, utilisé ou non.

- Informateur : seul le propriétaire propose une de ses cartes, et seul le
  destinataire reçoit l’information privée, dans sa propre vue Cartes & effets.
- Coordinateur : chacun choisit en secret une carte ; toutes les cartes choisies
  passent simultanément au voisin de gauche (ordre d’arrivée au salon).
- Arnaqueuse : tous confirment avoir mémorisé leur main avant la redistribution ;
  aucune carte n’est ajoutée depuis le paquet. Les anciennes mains de chacun
  restent consultables uniquement par leur ancien propriétaire.
- Hackeuse et Valet : le bénéficiaire choisit sa défausse en privé. Le Valet ajouté
  ne peut pas être défaussé et ne possède pas d’enseigne : aucune couleur ne peut
  le contenir. Avec les quatre valets ordinaires, il peut compter comme kicker.
- Gros bras : le bonus est inclus dans le score et dans le débrief ; il ne permet
  pas de battre une catégorie de combinaison supérieure.
- Scan rétinien et Lecteur d’empreintes : avant la dernière révélation, les autres
  joueurs valident tous la même estimation dans Cartes & effets. Le propriétaire
  du plus grand jeton rouge ne peut pas voter. Une estimation incorrecte fait
  échouer le braquage, même si l’ordre des jetons était correct ; le débrief
  indique cette cause d’échec.

Les choix et les effets sont validés sur l’hôte ; les requêtes spéciales identifient
la partie et le braquage pour refuser les anciennes commandes. Les mains cachées,
les choix simultanés et les informations privées ne sont pas diffusés aux autres.
Lors d’une relève, les difficultés, cartes actives et piles sont transférées ; le
braquage courant est redistribué comme dans le fonctionnement habituel du jeu.

## Illustrations

Les cartes affichées utilisent vingt compositions vectorielles originales dans
`TheGang/View/SpecialCardVisuals.cs` : cadres et typographies dorés, Effractions
bordeaux et Complices vert sombre, avec une illustration distincte par carte.
Elles restent nettes aux tailles du catalogue et en vue agrandie. Les anciennes
extractions du livret sont conservées comme références dans les sources, mais
ne sont plus embarquées ni affichées dans le jeu.

Les noms et effets restent fondés sur le livret français fourni. Le catalogue,
les fiches et les tutoriels expliquent le rôle de chaque famille et le moment
d’application des effets. Les ateliers sont des scénarios guidés indépendants,
pas des tirages aléatoires ; leurs conseils sur les cartes cachées sont réservés
au tutoriel.

## Décisions et radio de partie

Les cartes actives restent sur le tapis. Le volet d’effets flottant et repliable sur le tapis
présente les décisions privées, les propositions de pouvoirs, les votes et les
estimations sans ouvrir le catalogue. Les informations privées sont affichées
uniquement au destinataire. La Hackeuse identifie la carte piochée dans la défausse.
Les faces sombres conservent leur couronne colorée ; elles ne sont pas atténuées.

La radio à droite transmet des messages textuels authentifiés, de 1 à 280 caractères,
avec au plus un envoi par seconde et les 80 derniers messages en mémoire sur l’hôte.
Elle ne transmet aucune main automatiquement. Les règles de communication restent
applicables. Le chat n’est pas un journal persistant : il n’est pas transféré lors
d’une relève d’hôte. Les tutoriels sont hors ligne et désactivent la radio.

Les cartes connues avec certitude apparaissent directement à leur emplacement
chez leur propriétaire, uniquement pour les joueurs qui les connaissent : carte
montrée par l’Informateur, carte passée par le Coordinateur, ou Valet sans couleur
public. Une défausse met à jour cet emplacement ; une carte sortie de la main ne
reste pas révélée. Le mélange de l’Arnaqueuse ne révèle pas les nouveaux
propriétaires des cartes mémorisées. Les autres cartes restent cachées.

Les décisions flottent au-dessus du plateau sans changer ses dimensions. Les
pouvoirs facultatifs s’ouvrent à la demande ; les votes et décisions obligatoires
ouvrent automatiquement le volet, qui peut toujours être réduit.

# Bots et parties hors ligne

Depuis le menu, **Jouer hors ligne · Avec des bots** ouvre un salon local. Choisir
la capacité (3 à 6), puis ajouter 2 à 5 bots avant de lancer. Les mêmes boutons
permettent à l'hôte d'un salon en ligne de compléter son groupe ou de retirer les
bots avant le départ. Humains et bots occupent chacun une place. Les bots sont
nommés « Complice N » et signalés par « BOT » au salon et sur le plateau.

Aucune connexion externe ni aucun processus Cloudflared n'est nécessaire en hors
ligne. Le bouton de démarrage « Continuer sans mise à jour » permet d'accéder au
menu si la vérification réseau tarde ; `TheGang.exe --offline` la saute également.
Les règles et variantes existantes restent disponibles ; les bots n'ont aucun
réglage de difficulté propre. L'humain hôte garde les transitions de tour, les
révélations et les commandes de fin de braquage.

## Architecture

- `DesktopLobbySession.CreateOfflineAsync` réutilise le serveur HTTP lié à
  127.0.0.1 et son client habituel, sans publier de tunnel.
- `HostedLobby.Bots` gère les places et l'ordonnancement sur le seul serveur
  autoritaire. Les bots n'ont aucun jeton de session réseau. Les actions passent
  par `ApplyAs`, le même chemin de validation et d'événements que les humains.
- `BotObservation` copie la main du bot, les cartes communes, les jetons et
  confirmations publics, ainsi que sa projection autorisée des effets spéciaux.
  `BotPublicPlayer` ne possède aucun champ de cartes. Aucune référence à `Game`,
  au paquet, à une main adverse cachée ou à une autre décision n'est fournie.
- Chaque `BotDecision` possède sa propre source aléatoire et sa mémoire. Il estime
  un percentile par 96 distributions hypothétiques contre une main inconnue,
  réévalué à chaque révélation ou modification de sa main. Les jetons visibles
  influencent modérément son estimation. Les erreurs proviennent des informations
  manquantes et de l'échantillonnage, sans injection d'échecs ni oracle.
- Un bot sans jeton peut contester une attribution puis accepter la meilleure place
  libre s'il est dépossédé. Un bot déjà muni d'un jeton attend ou confirme : aucun
  retour au centre ni échange interdit. Cette stratégie évite les cycles.
- Les choix initiaux et défausses comparent les mains restantes ; les bots acceptent
  les votes Complice proposés par les humains. Ils ne proposent pas eux-mêmes les
  pouvoirs optionnels. E04/E09 permettent explicitement une discussion collective :
  les estimations sont donc publiques et affichées pour tous. Les bots rejoignent
  une proposition humaine, ou forment une estimation par échantillonnage, sans
  consulter la main ciblée ni faire participer son propriétaire.
- Le serveur sonde toutes les 250 ms, avec 1 à 2 secondes de réflexion par bot.
  Les décisions sont recalculées au moment d'agir ; seules des échéances sont
  conservées. Une nouvelle phase, partie ou étape obligatoire renouvelle le délai.
  Les pauses, départs, fermetures et relèves suspendent les bots. La destruction du
  serveur annule et attend sa boucle. Les bots acceptent les revanches ; la relève
  conserve leurs places et choisit toujours un successeur humain.

## Vérification

`BotTests` couvre les 16 configurations effectif/variante avec un humain, les
parties mixtes, les 20 cartes spéciales, les limites et autorisations du salon,
la revanche, la relève et la fermeture pendant la réflexion. Un test vérifie
qu'une modification des mains adverses dans un instantané ne change ni
l'observation ni la décision d'un bot à aléatoire égal. Un test HTTP vérifie le
parcours local sans tunnel. Les tests historiques du moteur et du rendu restent
applicables.

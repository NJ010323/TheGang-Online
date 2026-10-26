# Mises à jour Windows

## Analyse du projet et choix

`Model` contient les règles et l'état de la partie, `ViewModel` les adaptations de présentation, `Network` le serveur HTTP ASP.NET Core, le client et le tunnel Cloudflare, et `View` l'application WPF .NET 8. La distribution existante ne vise que Windows x64 : le script épingle cloudflared amd64. Les configurations x86 de la solution ne constituent pas une distribution x86. Le workflow précédent ciblait incorrectement `net8.0` pour WPF et omettait le tunnel ; il est remplacé.

Les sessions restent en mémoire. Les timers, le serveur et le tunnel ne démarrent qu'après une action du joueur. La fermeture existante dispose la session et son tunnel propre ; cette logique reste en place. Les mises à jour sont exécutées avant d'activer le menu et ne sont jamais déclenchées en cours de partie.

Les seules données persistantes trouvées sont le ticket invité `%LOCALAPPDATA%\TheGang\guest-resume.dat`, protégé par DPAPI pour le compte Windows courant. Les paramètres visuels sont en mémoire ; aucune migration de sauvegarde n'est nécessaire. Les mises à jour n'accèdent jamais au ticket, aux invitations ni aux jetons de session. Les fichiers personnels non possédés par le manifeste restent en place ; une collision sur un nouveau nom de fichier bloque l'installation. Les fichiers retirés d'un futur manifeste sont conservés, pour éviter de supprimer des données par erreur.

`Updates` est une bibliothèque .NET 8 sans dépendance externe, testable sans WPF. `Updater` est un petit exécutable autonome, sans interface normale. Il est copié dans le profil utilisateur avant fermeture et remplace également sa propre version distribuée. Aucun lanceur à ouvrir.

## Première distribution

Prérequis développeur : Windows et SDK .NET 8 ou plus récent, accès HTTPS à NuGet, GitHub et Cloudflare. Depuis la racine :

```powershell
dotnet test ./TheGang/UnitTests/UnitTests.csproj -c Release
./TheGang/publish-windows.ps1 -Version 1.0.0 -OutputDirectory ./TheGang/artifacts/release-1.0.0
Compress-Archive -Path ./TheGang/artifacts/release-1.0.0/* -DestinationPath ./TheGang/artifacts/TheGang-win-x64.zip
```

Choisir un dossier de sortie **vide** à chaque publication. Le script refuse d'écraser un dossier existant rempli. Il publie le jeu et l'auxiliaire avec leurs runtimes, inclut cloudflared vérifié, la configuration publique et `package.json` (version et SHA-256 de chaque fichier). Le contenu du ZIP doit être à la racine, sans sous-dossier.

Distribuer tout le dossier ou le ZIP. Chaque joueur extrait les fichiers dans un dossier où son compte peut écrire, par exemple sous Documents, puis ouvre `TheGang.exe`. Aucun SDK, Git, script ni installation du runtime côté joueur. Éviter Program Files pour permettre les mises à jour sans élévation. Aucun changement de visibilité ou publication distante n'est effectué par cette implémentation.

Une ancienne version sans cette fonctionnalité doit recevoir cette première distribution manuellement une seule fois. Les builds de développement sans `update-settings.json` voisin ignorent volontairement les mises à jour.

## Démarrage et récupération

1. Le menu habituel est affiché, temporairement désactivé, avec un bandeau discret. Alt+F4 peut annuler cette phase. La vérification GitHub est asynchrone et limitée à 10 secondes.
2. Le client consulte `/repos/{Repository}/releases/latest` sans jeton. Il ignore brouillons/préversions, compare numériquement les versions stables `vX.Y.Z` et n'accepte que `TheGang-win-x64.zip`. Les builds, tags et manifestes utilisent la même version.
3. Une nouvelle version est téléchargée en zone temporaire, avec progression et limite totale de cinq minutes. Le SHA-256 du ZIP est comparé au digest fourni par GitHub. Puis chaque fichier est vérifié contre le manifeste. L'extraction interdit chemins absolus, traversées, sous-dossiers, doublons, ADS NTFS et liens symboliques ; les tailles et nombres de fichiers sont bornés.
4. Le jeu lance sa copie temporaire de l'auxiliaire et attend sa confirmation avant de fermer proprement. L'auxiliaire attend la fin du PID exact, vérifie aussi son heure de création et n'arrête aucun processus existant.
5. Un mutex coordonne les démarrages et les auxiliaires. Les clients ouverts partagent un verrou de lecture par installation ; l'auxiliaire exige un verrou exclusif. Plusieurs clients peuvent donc jouer depuis le même dossier. S'ils restent ouverts, l'installation est abandonnée après attente, sans interrompre leurs parties. Un seul client par installation effectue la recherche à la fois.
6. L'auxiliaire revérifie le paquet, sauvegarde les fichiers existants, écrit et force sur disque un journal avant toute modification. Chaque remplacement utilise un fichier temporaire puis un renommage. Les fichiers personnels inconnus ne sont pas écrasés. L'auxiliaire relance le même `TheGang.exe` et attend jusqu'à 45 secondes que sa fenêtre confirme son initialisation.
7. Après confirmation, le journal est supprimé. En cas d'échec, les fichiers sauvegardés sont restaurés, les fichiers nouvellement ajoutés sont retirés et l'ancienne version redémarre avec un message français. Un tag échoué est mémorisé et ignoré : publier un nouveau numéro pour le corriger. Le redémarrage de validation et celui de récupération ne refont pas la recherche.
8. Après une interruption pendant les copies, le prochain démarrage détecte le journal et fait exécuter la restauration par la copie conservée de l'auxiliaire avant d'autoriser une partie. Cette récupération suppose un disque et un exécutable lisibles ; une panne matérielle ou une suppression manuelle peut demander la réextraction du ZIP précédent.

Une recherche échouée laisse jouer la version actuelle. Un téléchargement interrompu ne modifie pas l'installation. Les fichiers verrouillés ou les permissions insuffisantes font échouer l'installation avec restauration ; si celle-ci reste bloquée, un message indique de fermer les programmes concernés et de relancer le jeu. Il n'y a pas de demande automatique de droits administrateur.

Les journaux, la dernière sauvegarde, le journal de transaction et l'auxiliaire de secours sont dans `%LOCALAPPDATA%\TheGang\Updates\<identifiant du dossier installé>`. `update.log` indique étapes et types d'erreurs, sans URL d'invitation, jeton de session ou secret. Les fichiers téléchargés et la sauvegarde sont conservés pour diagnostic ; prévoir de l'espace disque pour le ZIP, les fichiers extraits et une copie des anciens fichiers. Ils peuvent être supprimés quand le jeu est fermé et qu'aucun `journal.json` n'est présent, mais conserver `runner` pour les opérations en attente.

## Intégrité et confiance

Le digest SHA-256 provient de l'API GitHub en HTTPS et couvre tout le ZIP. Le manifeste interne permet une seconde vérification des fichiers extraits. Le point de confiance est **le dépôt de distribution GitHub configuré**, ses responsables et son workflow. Cela détecte corruption et modification en transit ; cela ne protège pas contre un compte GitHub ou un workflow de publication compromis. Il n'y a pas de signature Authenticode indépendante ni de secret embarqué. Ne donner l'accès en écriture au dépôt de distribution qu'aux responsables de publication. Référence : [API officielle GitHub Releases](https://docs.github.com/en/rest/releases/releases).

## Publication des versions suivantes

Le workflow `.github/workflows/ci.yml` s'exécute sur un tag `vX.Y.Z` poussé sur GitHub. Il vérifie la syntaxe stable, exécute les tests, publie Windows x64 autonome avec le script existant corrigé, archive le paquet complet, crée une Release brouillon, téléverse le ZIP puis publie la Release. Une erreur d'envoi laisse le brouillon invisible aux clients. Un tag doit toujours désigner une version supérieure ; ne pas réutiliser ni modifier une Release déjà distribuée.

Après revue et commit des changements, par exemple :

```powershell
git tag v1.1.0
git push origin v1.1.0
```

Seul le job de publication reçoit `contents: write` ; la compilation a `contents: read`. Le ZIP porte un nom stable, son numéro vient du tag, son contenu embarque ce numéro. Aucun jeton n'est copié dans le paquet. Les clients requièrent un dépôt public ; l'absence de Release ou un dépôt inaccessible permet de démarrer hors ligne. Le workflow ne change jamais la visibilité du dépôt.

## Source privée et dépôt public distinct

1. Créer manuellement un dépôt public de distribution vide de code, initialisé avec un README. **Conserver le dépôt source privé.**
2. Modifier `TheGang/update-settings.json` : `{"Repository":"proprietaire/TheGang-Releases"}`. Cette configuration unique sert aussi de valeur par défaut aux publications locales.
3. Dans le dépôt source, définir la variable Actions `DISTRIBUTION_REPOSITORY` avec exactement la même valeur. Le workflow la transmet à la compilation et à la publication.
4. Définir le secret Actions `DISTRIBUTION_TOKEN` avec un jeton finement limité au dépôt public cible et à `Contents: read and write`. Le `GITHUB_TOKEN` du dépôt source ne peut pas publier dans un autre dépôt. Ce secret reste dans Actions. Aucun jeton n'est nécessaire pour les joueurs.
5. Pousser un nouveau tag stable. Le workflow téléverse seulement le ZIP compilé dans le dépôt public. Les éventuelles archives automatiques GitHub de ce dépôt ne contiennent que son README, pas le dépôt source.

Avec source publique et distribution sur le même dépôt, aucun secret supplémentaire n'est nécessaire ; garder configuration et dépôt du workflow cohérents. Le workflow refuse volontairement de distribuer dans le dépôt source s'il est privé et si aucun dépôt distinct n'est configuré.

## Vérifier une montée de version

Les tests `UpdateTests` utilisent un faux GitHub et de vrais fichiers temporaires : versions identiques/plus récentes, brouillons/préversions, absence réseau, téléchargement tronqué/corrompu, paquet altéré, traversées de chemins, restauration après copies partielles, fichiers verrouillés, conservation des fichiers personnels et absence de nouvelle tentative sur un tag échoué.

Pour un essai réel complet après configuration du dépôt : distribuer 1.0.0, publier `v1.1.0` via Actions, ouvrir l'ancien `TheGang.exe`, observer recherche/téléchargement/installation et vérifier que le même exécutable affiche la version 1.1.0 dans ses propriétés. Relancer : aucune réinstallation. Couper le réseau : le menu redevient disponible après au plus le délai de recherche. Fermer pendant un téléchargement : l'ancienne version reste intacte. Vérifier que le ticket invité est inchangé. Pour plusieurs clients depuis le même dossier, maintenir une partie ouverte : l'auxiliaire doit renoncer sans la couper.

Le script développeur `TheGang/test-update-windows.ps1` permet aussi des essais locaux des vrais exécutables, sans publier de Release. Il prend deux dossiers déjà publiés, prépare des copies isolées et teste succès, exécutable non démarrable et interruption simulée. Il ferme uniquement les processus qu'il a lancés depuis ses propres copies de test.

## En cas de problème

Fermer les clients concernés et les programmes verrouillant leur dossier, puis relancer `TheGang.exe`. Une restauration en attente est reprise automatiquement. Lire `update.log` dans le dossier de diagnostic correspondant. Ne pas supprimer `journal.json` ou `backup` tant qu'une restauration est en attente.

Si le jeu ne démarre plus du tout, extraire le ZIP de la dernière version fonctionnelle dans **un nouveau dossier utilisateur**, puis lancer son `TheGang.exe`. Le ticket invité reste dans le profil Windows et n'est pas perdu. Conserver le dossier défaillant et les journaux pour diagnostic. Une version échouée peut être retentée après résolution du problème en supprimant uniquement `failed-version.txt`, jeu fermé ; préférer publier une version corrigée avec un nouveau tag.

## Validation effectuée le 26 octobre 2026

- Compilation de la solution sous Windows : aucune erreur ni avertissement.
- Suite complète : 144 tests réussis.
- Publications autonomes Windows x64 en 1.0.0 et 1.1.0 réalisées localement.
- Installation réelle 1.0.0 vers 1.1.0, remplacement de l'auxiliaire et confirmation WPF : réussis ; menu vérifié visuellement.
- Paquet intègre avec exécutable volontairement non démarrable : ancienne version et auxiliaire restaurés.
- Interruption simulée après remplacement partiel : récupération automatique au prochain lancement réussie.
- Fichier personnel témoin préservé dans tous ces scénarios, aucun journal de transaction restant après réussite.

Aucune Release distante n'a été publiée et aucun paramètre GitHub n'a été modifié. Restent à effectuer après configuration : exécution du workflow sur un vrai tag, téléchargement de la Release publique par une ancienne installation, et essai multijoueur sur plusieurs machines. Les essais locaux WPF et les tests réseau existants ont été exécutés ; ils ne remplacent pas cet essai de distribution en conditions réelles.

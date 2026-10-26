# Installateur Windows pour les joueurs

## Ce que reçoit le joueur

Distribuer un seul fichier : `TheGang-Setup-<version>-win-x64.exe`. Le joueur l'ouvre, suit l'assistant en français et peut choisir un raccourci sur le Bureau. Le jeu apparaît aussi dans le menu Démarrer et dans les applications installées de Windows.

Par défaut, les fichiers vont dans `%LOCALAPPDATA%\Programs\TheGang`, pour le compte courant, sans demande de droits administrateur. Le paquet inclut le runtime .NET, le tunnel Cloudflare et l'auxiliaire de mise à jour : aucun autre logiciel à installer côté joueur. L'installateur cible Windows x64 (Windows 10 1809 minimum / Windows 11).

Après installation, le joueur utilise le raccourci **The Gang**. Les mises à jour suivantes passent toujours par le service intégré au démarrage ; il n'est pas nécessaire de retélécharger un installateur à chaque version. Une version portable précédemment utilisée reste distincte : le joueur doit ensuite utiliser le raccourci de la version installée. Les données de reprise restent dans le même profil Windows.

## Protection des parties et des données

- L'installation et la désinstallation demandent de fermer le jeu, sans arrêter automatiquement un processus ou une partie.
- Le jeu et son auxiliaire signalent leur présence à l'installateur. Le jeu attend brièvement la fermeture de l'assistant lorsqu'il est lancé depuis sa dernière page.
- Une restauration de mise à jour en attente bloque l'installation et la désinstallation : relancer d'abord le jeu pour terminer la récupération.
- Un installateur ancien ne remplace pas un jeu déjà mis à jour vers une version supérieure.
- Relancer le même installateur permet une réinstallation des fichiers de cette version.
- La désinstallation enlève les fichiers installés, les raccourcis et l'entrée Windows. Elle ne supprime ni le ticket de reprise, ni les journaux dans `%LOCALAPPDATA%\TheGang`, ni les fichiers personnels ajoutés dans le dossier du jeu. Des fichiers ajoutés par de futures versions peuvent rester dans ce dossier s'ils n'ont jamais été enregistrés par l'installateur ; aucune suppression récursive de données inconnues n'est effectuée.

## Construire un installateur

Prérequis développeur : Windows et SDK .NET 8 ou plus récent. Exemple pour une nouvelle version, depuis la racine :

```powershell
./TheGang/publish-windows.ps1 -Version 1.0.5 -OutputDirectory ./TheGang/artifacts/release-1.0.5
./TheGang/build-installer.ps1 -PublishDirectory ./TheGang/artifacts/release-1.0.5
```

Le premier dossier doit être vide. Le résultat est dans `TheGang/artifacts/installer`. Le numéro de l'installateur est lu dans `package.json`, comparé à la version embarquée du jeu, et les empreintes de chaque fichier sont vérifiées avant compilation. Le script refuse d'écraser un installateur existant de la même version.

Le compilateur officiel **Inno Setup 6.7.3** est préparé automatiquement en mode portable sous `TheGang/artifacts/tools`, sans installation système ni association de fichiers. Son téléchargement est épinglé par SHA-256 et sa signature éditeur est vérifiée avant exécution. Le compilateur n'est pas distribué aux joueurs. Un autre compilateur compatible peut être fourni explicitement par `-CompilerPath`.

Sources et licence du compilateur : [site officiel Inno Setup](https://jrsoftware.org/isdl.php), [sources de son mode portable](https://github.com/jrsoftware/issrc/blob/main/isportable.iss). La préparation fournie utilise l'édition non commerciale ; consulter les conditions de l'éditeur avant un usage commercial.

Le script `Installer/TheGang.iss` liste explicitement les cinq fichiers du paquet et son manifeste. Si de nouveaux fichiers deviennent nécessaires au jeu, adapter cette liste et celle de `build-installer.ps1` ensemble.

## GitHub Releases

Le workflow de tag `vX.Y.Z` construit maintenant deux fichiers :

- `TheGang-Setup-X.Y.Z-win-x64.exe` : première installation pour les joueurs.
- `TheGang-win-x64.zip` : paquet portable et mises à jour automatiques.

Les deux utilisent la même version issue du tag et le même dépôt de distribution configuré. Le ZIP reste obligatoire : le jeu ne télécharge pas l'installateur pour se mettre à jour. Avec un dépôt source privé, les deux fichiers compilés peuvent être publiés dans le dépôt public distinct décrit dans [le guide des mises à jour](mises-a-jour.md).

Aucune Release distante n'est créée pendant la préparation locale. Il faut pousser un tag après revue et commit pour déclencher la publication.

## Signature et distribution

Le fichier Setup produit localement n'est pas signé avec un certificat de signature de code. La signature vérifiée lors de la préparation concerne uniquement le compilateur officiel, pas le jeu ou son installateur. Une signature éditeur du jeu et du Setup reste à configurer si une distribution signée est souhaitée ; ne jamais placer la clé privée d'un certificat dans le dépôt ou le paquet joueur.

## Vérifier l'installateur

```powershell
./TheGang/Installer/test-installer.ps1 -PublishDirectory ./TheGang/artifacts/release-1.0.4 -PreviousPublishDirectory ./TheGang/artifacts/release-1.0.3
```

Ce script compile des installateurs de test avec une identité Windows et des noms de raccourcis uniques. Il utilise un dossier isolé, vérifie installation, raccourcis, réinstallation, refus d'une version antérieure, protection du jeu ouvert, détection de restauration en attente (y compris dans un chemin accentué), conservation du désinstallateur pendant les copies de l'updater, désinstallation et conservation de fichiers personnels témoins. Il ne remplace pas une installation réelle du joueur. Les journaux de test et `result.json` sont conservés sous `TheGang/artifacts/installer-test-*`.

Le même contrôle est exécuté dans GitHub Actions. Sans ancien paquet local, le test génère un installateur de fixture portant une version inférieure uniquement pour vérifier son refus avant toute copie ; ces fichiers de test ne sont jamais publiés.

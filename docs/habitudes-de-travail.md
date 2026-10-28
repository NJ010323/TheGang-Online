# Habitudes de travail sur The Gang Online

Ce texte formalise notre fonctionnement pour modifier le jeu, le tester et le distribuer.

## Modifications du jeu

- Travailler dans le dossier du projet actif et lire ses consignes `AGENTS.md` avant de modifier les fichiers. Vérifier l’état Git et préserver les modifications déjà présentes.
- Réaliser les changements demandés jusqu’au bout : logique, réseau, interface, textes explicatifs et documentation concernés.
- Respecter le style du jeu : tapis, cadres et accents dorés, polices cohérentes, contrôles personnalisés et animations existantes. Éviter les contrôles Windows visuellement discordants.
- Les boutons de fermeture affichent « Fermer », sans croix ajoutée ni mention d’Échap. Ne pas afficher d’infobulles Windows grises au survol.
- Intégrer les décisions de partie sans redimensionner le plateau. Garder les cartes actives accessibles et respecter la confidentialité des mains et des informations reçues.
- Toute carte adverse connue avec certitude s’affiche à son emplacement chez son propriétaire, uniquement pour les joueurs qui la connaissent. Ne pas transformer un renseignement partiel ou un mélange caché en révélation supplémentaire.

## Vérification après modification

- Compiler et effectuer les vérifications adaptées au changement. Tester réellement les règles, les transitions, le réseau et la confidentialité lorsqu’ils sont concernés.
- Pour une correction visuelle, examiner le rendu ; pour les changements de disposition, vérifier les grandes et petites fenêtres. Fournir des captures quand elles sont demandées ou utiles pour juger le résultat.
- Ajouter une vérification de régression lorsqu’un défaut fonctionnel ou de disposition reproductible est corrigé. Ne pas ajouter des tests artificiels pour une simple couleur.
- Signaler ce qui a été vérifié et les éventuelles limites. Ne pas annoncer un build, un push ou une release avant confirmation de sa réussite.

## Version et build local

- Après chaque modification du jeu, régénérer la distribution Windows autonome dans `TheGang/artifacts/gametest` pour permettre un test immédiat.
- Avant le build, consulter la dernière release stable publiée sur le dépôt de distribution configuré dans `TheGang/update-settings.json`. Utiliser sa version, même si les sources contiennent des corrections supplémentaires.
- Quand une nouvelle version est explicitement demandée, utiliser cette version pour sa publication. Après sa sortie, les builds de travail suivants reprennent son numéro tant qu’aucune autre release n’est publiée.
- Ne pas augmenter une version, créer un tag ou publier une release uniquement parce qu’un fichier a été modifié.
- Construire avec `TheGang/publish-windows.ps1` dans un dossier temporaire vide, vérifier le manifeste SHA-256 et les versions du jeu et de l’auxiliaire, puis copier le paquet vérifié vers `gametest`.
- Préserver les fichiers personnels éventuels et ne jamais interrompre un jeu en cours pour remplacer ses exécutables. Si le jeu bloque la copie, terminer le travail préparatoire puis demander sa fermeture.
- Une modification limitée à la documentation ou aux consignes ne nécessite pas de reconstruire le jeu.

## Commits et push

- Lorsqu’un commit est demandé, utiliser un titre français précis avec un préfixe cohérent : `fix(ui): …`, `feat(…): …` ou, pour un commit de release, `fix(v0.1.1): …` par exemple.
- Rédiger une description expliquant le problème, le comportement obtenu, les changements importants et les vérifications effectuées.
- Vérifier les fichiers inclus pour ne pas embarquer des exécutables de test, des caches, des secrets ou des fichiers temporaires.
- Pousser les modifications lorsque le push est demandé. Vérifier sa réussite et communiquer le hash du commit et la branche utilisée.
- Une correction courante n’entraîne pas automatiquement un nouveau tag de version ou une nouvelle release.

## Nouvelle release

- Préparer un patch note couvrant tous les changements depuis la précédente release publiée, y compris les commits intermédiaires et les corrections réalisées après leur push.
- Enregistrer les notes dans `docs/releases/vX.Y.Z.md`. Ces notes alimentent à la fois la release GitHub et l’historique intégré au jeu.
- Vérifier les changements, créer le commit descriptif et le tag annoté `vX.Y.Z`, puis les pousser.
- Attendre la réussite de la chaîne GitHub Actions : tests, contrôles WPF, paquet autonome, installateur et validation de son cycle d’installation.
- Vérifier que la release est réellement publiée et contient le ZIP portable complet ainsi que l’installateur Windows correspondant. Une publication standard est stable et devient la dernière release active ; une bêta exige une demande explicite.

## Fichiers de distribution et nettoyage

- Pour chaque nouvelle release, conserver directement le paquet Windows dans `TheGang/artifacts/release_vX.Y.Z`.
- L’archive de release et `gametest` doivent contenir les mêmes fichiers de distribution au moment de la publication : jeu, auxiliaire de mise à jour, tunnel, licence, configuration et manifeste.
- Récupérer le ZIP effectivement publié par GitHub, vérifier son empreinte, puis vérifier chaque fichier à l’aide de son manifeste. Utiliser ce paquet exact pour l’archive et `gametest`, même si un build local du même numéro existe déjà.
- Conserver les anciennes archives `release_v*` et ne pas les remplacer par des corrections ultérieures. `gametest` est le dossier de travail régénéré ; `release_vX.Y.Z` est la copie du paquet publié.
- Nettoyer les dossiers temporaires de build et les fichiers temporaires créés pour les commits ou les téléchargements, après vérification de la copie finale. Garder les captures utiles dans un dossier distinct.
- Ne pas supprimer les fichiers indispensables au lancement, les fichiers personnels ou les outils sans avoir identifié leur rôle. Vérifier les chemins et le contenu avant tout nettoyage.

## Compte rendu

- Indiquer brièvement ce qui a changé, ce qui a été testé et où se trouve le build.
- En cas de commit ou de release, fournir le hash, le tag et le lien de publication lorsque ces opérations ont réussi.
- Fournir des captures pour les modifications visuelles lorsque cela aide à vérifier le résultat.

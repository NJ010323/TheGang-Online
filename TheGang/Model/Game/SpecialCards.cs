namespace Model.Game;

public enum GameDifficulty { Standard, Advanced, Pro, Gangster }
public sealed record SpecialCardDefinition(string Id, int Number, bool IsComplice, string Name, string Rule, string Timing)
{
    public string Family => IsComplice ? "Complice" : "Effraction";
}
public static class DifficultyRules
{
    public static string Name(GameDifficulty mode) => mode switch { GameDifficulty.Advanced => "Avancé", GameDifficulty.Pro => "Pro", GameDifficulty.Gangster => "Gangster", _ => "Standard" };
    public static string Description(GameDifficulty mode) => mode switch
    {
        GameDifficulty.Advanced => "Premier braquage sans carte spéciale. Ensuite, une Effraction après une réussite ou un Complice après un échec, pour un seul braquage. Les piles commencent dans l’ordre 1 à 10 pour les découvrir. Victoire : 3 coffres ; défaite : 3 alarmes.",
        GameDifficulty.Pro => "Comme Avancé, avec une Effraction tirée au hasard et active toute la partie, dès le premier braquage. À partir du deuxième : deux cartes actives. Ouverture facile est retirée. Victoire : 3 coffres ; défaite : 3 alarmes.",
        GameDifficulty.Gangster => "Deux Effractions aléatoires dès le premier braquage. Avant chaque suivant, celle au plus petit numéro est remplacée. Aucun Complice, ni Ouverture facile. Victoire : 3 coffres ; défaite dès 2 alarmes !",
        _ => "Le jeu de base, sans cartes spéciales. Victoire : 3 coffres ; défaite : 3 alarmes. Recommandé pour découvrir le jeu. Plus la bande est nombreuse, plus le classement est difficile."
    };
}
public static class SpecialCards
{
    private static SpecialCardDefinition E(int n,string name,string rule,string timing) => new($"E{n:00}",n,false,name,rule,timing);
    private static SpecialCardDefinition C(int n,string name,string rule,string timing) => new($"C{n:00}",n,true,name,rule,timing);
    public static IReadOnlyList<SpecialCardDefinition> All { get; } = new[] {
        E(1,"Ouverture facile","Après la distribution, sautez les jetons blancs et révélez immédiatement le flop. Cette carte est absente des modes Pro et Gangster.","Distribution"),
        E(2,"Capteurs de bruit","Les jetons 1 blancs, jaunes et orange deviennent sombres : une fois pris, ils ne peuvent plus changer de propriétaire. Les jetons rouges restent libres.","Tours 1 à 3"),
        E(3,"Détecteurs de mouvement","Si le flop contient au moins un valet, une dame ou un roi, le détenteur du jeton blanc 1 défausse sa main et reçoit deux nouvelles cartes.","Au flop"),
        E(4,"Scan rétinien","Avant la dernière main de l’abattage, les autres joueurs doivent convenir du rang, de 2 à As, de l’une des cartes personnelles du détenteur du plus grand jeton rouge. Ce joueur ne participe pas. Une erreur fait échouer le braquage.","Avant la dernière révélation"),
        E(5,"Fuite précipitée","Après le flop, révélez la quatrième puis la cinquième carte commune sans répartir de jetons orange. Passez directement aux jetons rouges.","Après le flop"),
        E(6,"Grille d’aération","Les jetons les plus élevés blancs, jaunes et orange deviennent sombres : une fois pris, ils ne peuvent plus changer de propriétaire. Les jetons rouges restent libres.","Tours 1 à 3"),
        E(7,"Barrières photoélectriques","Si le flop ne contient aucun valet, dame ou roi, le détenteur du plus grand jeton blanc défausse sa main et reçoit deux nouvelles cartes.","Au flop"),
        E(8,"Panne d’électricité","À chaque nouveau tour, les jetons des tours précédents disparaissent. Mémorisez les signaux : le tableau et le journal ne conservent pas leurs anciennes valeurs.","À chaque changement de tour"),
        E(9,"Lecteur d’empreintes digitales","Avant la dernière main de l’abattage, les autres joueurs doivent convenir de sa catégorie de combinaison, de carte haute à quinte flush royale. Son propriétaire ne participe pas. Une erreur fait échouer le braquage.","Avant la dernière révélation"),
        E(10,"Caméras de surveillance","Chacun reçoit une troisième carte personnelle. La meilleure combinaison utilise toujours cinq cartes, choisies parmi les trois privées et les cinq communes.","Distribution"),
        C(1,"Informateur","Un joueur montre secrètement une seule carte personnelle à un autre. Seul ce destinataire reçoit l’information ; il ne peut pas la communiquer aux autres.","Une fois, avant l’abattage, après accord unanime"),
        C(2,"Conductrice","Un joueur annonce uniquement la catégorie de sa meilleure combinaison actuelle, sans préciser les cartes qui la composent.","Une fois, avant l’abattage, après accord unanime"),
        C(3,"Actionnaire","Juste après la distribution, chacun annonce son nombre de têtes : valets, dames et rois. Les as ne sont pas des têtes.","Automatique à la distribution"),
        C(4,"Cerveau de la bande","Choisissez un rang de 2 à As et un joueur : il annonce combien de cartes de ce rang il possède.","Une fois, avant l’abattage, après accord unanime"),
        C(5,"Hackeuse","Un joueur pioche une carte supplémentaire, puis choisit secrètement une carte à défausser. Il peut défausser la carte qu’il vient de recevoir.","Une fois, avant l’abattage, après accord unanime"),
        C(6,"Coordinateur","Chacun choisit secrètement une carte personnelle. Lorsque tous ont choisi, toutes les cartes sélectionnées passent simultanément au voisin de gauche, selon l’ordre d’arrivée au lobby.","Obligatoire après la distribution"),
        C(7,"Valet","Un joueur reçoit un valet sans enseigne et défausse une autre carte personnelle. Ce valet compte comme J, ne peut pas former une couleur et sert de kicker avec les quatre autres valets.","Une fois, avant l’abattage, après accord unanime"),
        C(8,"Génie des chiffres","Chacun annonce la somme de ses cartes : 2 à 10 valent leur rang, J/D/R valent 10 et As vaut 11.","Automatique à la distribution"),
        C(9,"Arnaqueuse","Chacun regarde et mémorise sa main puis confirme. Seules les cartes personnelles déjà distribuées sont mélangées et redistribuées, sans carte prise dans le paquet.","Obligatoire après lecture de toutes les mains"),
        C(10,"Gros bras","Le joueur choisi bat toutes les autres mains de la même catégorie, indépendamment des valeurs. Une catégorie supérieure le bat toujours.","Une fois, avant l’abattage, après accord unanime")
    };
    public static SpecialCardDefinition Get(string id) => All.Single(c => c.Id == id);
}

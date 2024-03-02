module X4.Ships

open System
open System.Xml
open System.Xml.Linq


let rand = new Random(12345)    // Seed the random number generator so we get the same results each time, as long as we're not adding new regions or changing territory order.

// List of all ships that are valid candidates for being generated as abandoned ships.
let abandonedShipsList =
    // Our filters. Factions we'll look for, and tags we'll omit. Both must be true.
    let factions = [ "_arg_"; "_par_"; "_tel_"; "_spl_"; "_ter_"; "_atf_"; "_yak_"; "_pir_"; "_bor_";]
    let omit = ["_xs_"; "_plot_"; "_landmark_"; "_story"; "_highcapacity_"; "ship_spl_xl_battleship_01_a_macro"; "ship_pir_xl_battleship_01_a_macro"]

    X4.Data.allShipMacros
    |> List.filter (
        fun shipName ->
            ( factions |> List.exists (fun tag -> shipName.Contains tag) )        // Must contain one of the factions we're interested in.
            && (not ( omit |> List.exists (fun tag -> shipName.Contains tag) )))  // Must not contain any of the tags we're not interested in from the omit list. 


// Quickly filter by a search string substring. eg, 'bor' will return all the Boron ships by filtering for '_bor_'.
let rec filterListBy (searchTags:string list) (ships:string list) =
    match searchTags with
    | [] -> ships
    | H::T ->
        ships
        |> List.filter (fun x -> x.Contains("_" + H.ToLower() + "_"))
        |> filterListBy T

// Filter the abandoned ships list by a list of search tags. eg, ['bor', 's'] will return all Boron small ships.
// ['tel', 'xl', 'carrier'] will return all Teladi extra large carriers.
let filterBy (search:string list) =
    filterListBy search abandonedShipsList

let militaryShips =
    List.concat [
        filterBy ["corvette"]
        filterBy ["gunboat"]
        filterBy ["frigate"]
        filterBy ["destroyer"]
        filterBy ["battleship"]
        filterBy ["carrier"]
        filterBy ["resupplier"]
        filterBy ["fighter"]
        filterBy ["heavyfighter"]
        filterBy ["bomber"]
        filterBy ["scout"]
    ]

let economyShips =
    List.concat [
        filterBy ["miner"]
        filterBy ["builder"]
        filterBy ["trans"]
    ]

let generateRandomAbandonedShipFromListInSector (sector:string) (shipList:string list) =
    let ship = shipList.[rand.Next(shipList.Length)]
    // generate random coordinates within the sector, in KM offset from sector center (different from other coordinates)
    let x, y, z = rand.Next(-160, 160), rand.Next(-10, 10), rand.Next(-180, 180)
    // generate random yaw and pitch
    let yaw, pitch, roll = rand.Next(-180, 180), rand.Next(-180, 180), rand.Next(-180, 180)
    printfn "GENERATING ABANDONED SHIP: %s, Sector: %s, Position: %A, Rotation: %A" ship sector (x, y, z) (yaw, pitch, roll)
    (ship, sector, (x, y, z), (yaw, pitch, roll))

let generateRandomAbandonedShipFromList (shipList:string list) =
    let sector = X4.Data.selectRandomUnsafeSector() // We don't want these wrecks to be in the faction sectors.
    generateRandomAbandonedShipFromListInSector sector.Name shipList

let generateRandomMilitaryAbandonedShips (count:int) (size:string) =
    let ships = filterListBy [size] militaryShips
    [ for i in 1..count -> generateRandomAbandonedShipFromList ships ]

let generateRandomEconomyAbandonedShips (count:int) (size:string) =
    let ships = filterListBy [size] economyShips
    [ for i in 1..count -> generateRandomAbandonedShipFromList ships ]

// Generate the XML cue for creating an abandoned ship of the given class in the sector.
let ProcessShip ((ship:string), (sector:string), position, rotation) =
    // Interestingly, the units of KM and deg are specified in the XML attribute fields for abandoned ships.
    // I've not seen this elsewhere, and don't know if it's necessary, but for safety I'll duplicate it.
    let ((x:int), (y:int), (z:int)), ((yaw:int), (pitch:int), (roll:int)) = position, rotation
    let xml = $"""
        <add sel="/mdscript[@name='PlacedObjects']/cues/cue[@name='Place_Claimable_Ships']/actions">
        <find_sector name="$sector" macro="macro.{sector}"/>
        <do_if value="$sector.exists">
          <create_ship name="$ship" macro="macro.{ship}" sector="$sector">
            <owner exact="faction.ownerless"/>
            <position x="{x}KM" y="{y}KM" z="{z}KM"/>
            <rotation yaw="{yaw}deg" pitch="{pitch}deg" roll="{roll}deg"/>
          </create_ship>
        </do_if>
    </add>
    """
    // Using the textreader instead of XElement.Parse preserves whitespace and carriage returns in our output.
    let xtr = new XmlTextReader(new System.IO.StringReader(xml));
    XElement.Load(xtr);

// Create a list of random ships, assign them to random sectors, then generate XML that will place
// them as abandoned ships in the game.
let generate_abandoned_ships_file (filename:string) =

    let shipDiff =  List.concat [
        // A bunch of ships in unsafe space to being
        generateRandomMilitaryAbandonedShips 5 "XL" |> List.map ProcessShip
        generateRandomMilitaryAbandonedShips 10 "L" |>  List.map ProcessShip
        generateRandomMilitaryAbandonedShips 15 "m" |> List.map ProcessShip
        generateRandomMilitaryAbandonedShips 20 "s" |> List.map ProcessShip
        generateRandomEconomyAbandonedShips  4  "XL" |> List.map ProcessShip
        generateRandomEconomyAbandonedShips 10 "L" |> List.map ProcessShip
        generateRandomEconomyAbandonedShips 20 "m" |> List.map ProcessShip
        generateRandomEconomyAbandonedShips 30 "s" |> List.map ProcessShip
        [filterBy ["spl"; "xl"; "carrier"] |> generateRandomAbandonedShipFromList |> ProcessShip]      // Make sure there's at least one Raptor!
        [filterBy ["atf"; "xl"; "battleship"] |> generateRandomAbandonedShipFromList |> ProcessShip]   // And Asgard!
        [filterBy ["atf"; "l"; "destroyer"] |> generateRandomAbandonedShipFromList |> ProcessShip]     // And Syn.

        // followed by a handful of M in safe space.
        [
            for i in 1..5 ->
                militaryShips |> filterListBy ["m"] |> (generateRandomAbandonedShipFromListInSector (X4.Data.selectRandomSafeSector().Name)) |> ProcessShip
            for i in 1..5 ->
                economyShips |> filterListBy ["m"] |> (generateRandomAbandonedShipFromListInSector (X4.Data.selectRandomSafeSector().Name)) |> ProcessShip
        ]
    ]

    // Create the new XML Diff document to contain our region additions
    let diff = XElement.Parse(
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>
        <diff>
        </diff>
        ")

    // Now add the region changes, one by one, to the the xml diff.
    [| for element in shipDiff do
        diff.Add(element)
        diff.Add( new XText("\n")) // Add a newline after each element so the output is readible
    |] |> ignore

    Utilities.write_xml_file filename diff

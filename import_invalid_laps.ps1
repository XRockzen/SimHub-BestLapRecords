param(
    # Your name exactly as it appears in LMU results (check an XML file in LmuResultDir)
    [Parameter(Mandatory=$true)]
    [string]$PlayerName,

    [string]$GameFolder   = "LMU",
    [string]$LmuResultDir = "F:\Program Files (x86)\Steam\steamapps\common\Le Mans Ultimate\UserData\Log\Results",
    [string]$SimHubData   = "F:\Program Files (x86)\SimHub\PluginsData"
)

# LiteDB.dll lives in the SimHub root folder, one level above PluginsData
$simHubRoot = Split-Path $SimHubData -Parent
$liteDbPath = Join-Path $simHubRoot "LiteDB.dll"
if (-not (Test-Path $liteDbPath)) { Write-Error "LiteDB.dll not found at: $liteDbPath"; exit 1 }
Add-Type -Path $liteDbPath

$simhubDb  = Join-Path $SimHubData "$GameFolder\DataStore.db"
$suppDbDir = Join-Path $SimHubData "Common\BestLapRecords"
$suppDb    = Join-Path $suppDbDir "invalid_laps.db"

if (-not (Test-Path $simhubDb))     { Write-Error "SimHub DB not found: $simhubDb"; exit 1 }
if (-not (Test-Path $LmuResultDir)) { Write-Error "LMU Results dir not found: $LmuResultDir"; exit 1 }
New-Item -ItemType Directory -Force -Path $suppDbDir | Out-Null

# Load all SimHub valid laps (track, car, date)
Write-Host "Loading SimHub valid laps..."
$simhubLaps = @()
$db = New-Object LiteDB.LiteDatabase("Filename=$simhubDb;ReadOnly=true")
foreach ($doc in $db.GetCollection("ptp_datarecords").FindAll()) {
    if ($doc.ContainsKey("IsReplay") -and $doc["IsReplay"].AsBoolean) { continue }
    $lapTime = 0L
    if ($doc.ContainsKey("LapTime")) { $lapTime = $doc["LapTime"].AsInt64 }
    if ($lapTime -le 0) { continue }
    $trk  = if ($doc.ContainsKey("TrackName")) { $doc["TrackName"].AsString } else { "" }
    $car  = if ($doc.ContainsKey("CarId"))     { $doc["CarId"].AsString }     else { "" }
    $date = if ($doc.ContainsKey("RecordDate")) { $doc["RecordDate"].AsDateTime } else { [DateTime]::MinValue }
    if ($trk -and $car -and $date -ne [DateTime]::MinValue) {
        $simhubLaps += [PSCustomObject]@{ Track=$trk; Car=$car; Date=$date }
    }
}
$db.Dispose()
Write-Host "  $($simhubLaps.Count) valid laps from SimHub DB."

# Open supplementary DB
$suppConn = New-Object LiteDB.LiteDatabase("Filename=$suppDb")
$suppCol  = $suppConn.GetCollection("invalid_laps")

# Build dedup set
$seen = New-Object "System.Collections.Generic.HashSet[string]"
foreach ($d in $suppCol.FindAll()) {
    $k = ($d["Game"].AsString + "|" + $d["Track"].AsString + "|" + $d["Car"].AsString + "|" + $d["LapNumber"].AsInt32.ToString() + "|" + $d["Session"].AsString)
    $seen.Add($k) | Out-Null
}
Write-Host "  $($seen.Count) existing entries in supplement DB."

$escapedName = [regex]::Escape($PlayerName)
$totalAdded  = 0
$totalSkip   = 0

$xmlFiles = Get-ChildItem $LmuResultDir -Filter "*.xml" | Sort-Object Name
Write-Host "Processing $($xmlFiles.Count) XML files..."

foreach ($xmlFile in $xmlFiles) {
    $sessionId = [System.IO.Path]::GetFileNameWithoutExtension($xmlFile.Name)

    try {
        [xml]$xmlDoc = Get-Content $xmlFile.FullName -Encoding UTF8
    } catch {
        continue
    }

    $rr = $xmlDoc.rFactorXML.RaceResults
    if (-not $rr) { continue }
    $trackCourse = $rr.TrackCourse
    if (-not $trackCourse) { continue }

    foreach ($node in $rr.ChildNodes) {
        if ($node.NodeType -ne "Element") { continue }
        if (-not $node.Stream)            { continue }

        # Parse session start time
        $sessStart = [DateTime]::MinValue
        if ($node.TimeString -match '(\d{4})/(\d{2})/(\d{2}) (\d{2}):(\d{2}):(\d{2})') {
            $sessStart = [DateTime]::new([int]$Matches[1],[int]$Matches[2],[int]$Matches[3],[int]$Matches[4],[int]$Matches[5],[int]$Matches[6])
        }
        $sessEnd = $sessStart.AddHours(4)

        # Collect invalid lap numbers for this player
        $badLaps = @()
        foreach ($score in $node.Stream.Score) {
            if (-not ($score -is [System.Xml.XmlElement])) { continue }
            $txt = $score.'#text'
            if (-not $txt) { continue }
            if ($txt -match "^$escapedName\(\d+\)\s+lap=(\d+)\s+point=0\s+t=-") {
                $completed = ([int]$Matches[1]) - 1
                if ($completed -ge 0) { $badLaps += $completed }
            }
        }
        if ($badLaps.Count -eq 0) { continue }

        # Find CarId from SimHub DB (laps on this track near this session)
        $lo = $sessStart.AddHours(-2)
        $hi = $sessEnd.AddHours(2)
        $candidates = $simhubLaps | Where-Object { ($_.Track -eq $trackCourse) -and ($_.Date -ge $lo) -and ($_.Date -le $hi) }
        if (-not $candidates) {
            Write-Host ("  [SKIP] {0} / {1} - {2} : no matching car found" -f $xmlFile.Name, $node.LocalName, $trackCourse)
            $totalSkip += $badLaps.Count
            continue
        }
        $carId = ($candidates | Group-Object Car | Sort-Object Count -Descending | Select-Object -First 1).Name

        # Insert missing laps
        $added = 0
        foreach ($lapNum in $badLaps) {
            $k = ($GameFolder + "|" + $trackCourse + "|" + $carId + "|" + $lapNum.ToString() + "|" + $sessionId)
            if ($seen.Contains($k)) { continue }

            $bDoc = New-Object LiteDB.BsonDocument
            $bDoc.set_Item("Game",      (New-Object LiteDB.BsonValue([string]$GameFolder)))
            $bDoc.set_Item("Track",     (New-Object LiteDB.BsonValue([string]$trackCourse)))
            $bDoc.set_Item("Car",       (New-Object LiteDB.BsonValue([string]$carId)))
            $bDoc.set_Item("LapNumber", (New-Object LiteDB.BsonValue([int]$lapNum)))
            $bDoc.set_Item("Session",   (New-Object LiteDB.BsonValue([string]$sessionId)))
            $bDoc.set_Item("Date",      (New-Object LiteDB.BsonValue([DateTime]$sessStart)))
            $suppCol.Insert($bDoc) | Out-Null
            $seen.Add($k) | Out-Null
            $added++
            $totalAdded++
        }

        if ($added -gt 0) {
            Write-Host ("  [OK]   {0} / {1} - {2} invalid laps added for {3}" -f $xmlFile.Name, $node.LocalName, $added, $carId)
        }
    }
}

$suppConn.Dispose()
Write-Host ""
Write-Host ("Done. Added: {0} invalid laps  |  Skipped: {1}" -f $totalAdded, $totalSkip)


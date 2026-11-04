namespace Site

open System
open WebSharper
open WebSharper.JavaScript
open WebSharper.D3

/// A translation of mbostock's "World Tour" example to WebSharper:
/// http://bl.ocks.org/mbostock/4183330
///
/// The WebSharper.D3 binding targets D3 v7 for selections and projections, but
/// does not expose d3.geoPath / d3.geoCentroid and its transition API is v3-era,
/// so those calls use JavaScript interop.

[<AutoOpen>]
[<JavaScript>]
module D3Extras =

    /// Turns a callback-based D3 call into an async computation.
    let Defer (job: (obj * 'T -> unit) -> unit) : Async<'T> =
        Async.FromContinuations(fun (ok, no, _) ->
            job (fun (err, data) -> if As err then no (exn "Defer: failed") else ok data))

    type D3 with
        static member Json(url: string) = Defer(fun c -> D3.Json(url, c))
        static member Tsv(url: string) = Defer(fun c -> D3.Tsv(url, c))

[<JavaScript>]
module WorldTour =

    [<Name "">]
    type ITopoJson =
        abstract feature: topology: obj * geoObject: obj -> obj
        abstract mesh: topology: obj * geoObject: obj * filter: (obj * obj -> bool) -> obj

    /// The topojson library, loaded as a global from topojson.v1.min.js.
    let topojson: ITopoJson = JS.Global?topojson

    let private d3: obj = JS.Global?d3
    let private geoPath: unit -> Path = As (d3?geoPath)
    let private geoCentroid: obj -> float [] = As (d3?geoCentroid)

    [<AbstractClass>]
    type Country =
        inherit D3.Feature

    let Render (ctx: Dom.Element) =
        let width = 960.
        let height = 500.
        let projection = D3.GeoOrthographic().Scale(248.).ClipAngle(90.)
        let title = D3.Select(ctx).Append("h1")
        let canvas = D3.Select(ctx).Append("canvas").Attr("width", width).Attr("height", height)
        let c = (canvas.Node() |> As<CanvasElement>).GetContext("2d")
        let path = (geoPath()).Projection(projection).Context(c)

        async {
            let globe = New [ "type" => "sphere" ]
            let! world = D3.Json("world-110m.json")
            let! names = D3.Tsv("world-country-names.tsv")

            let land = topojson.feature(world, world?objects?``land``)
            let borders = topojson.mesh(world, world?objects?countries, fun (a, b) -> a !==. b)
            let countries =
                topojson.feature(world, world?objects?countries)?features
                |> Array.filter (fun d ->
                    names |> Array.exists (fun n ->
                        if d?id ==. n?id then
                            d?name <- n?name
                            true
                        else false))
                |> Array.sortBy (fun d -> d?name)
                |> As<Country[]>

            let draw (country: Country) =
                c.ClearRect(0., 0., width, height)
                c.FillStyle <- "#c7d2fe"; c.BeginPath(); path.Call(land) |> ignore; c.Fill()
                c.FillStyle <- "#4f46e5"; c.BeginPath(); path.Call(country) |> ignore; c.Fill()
                c.StrokeStyle <- "#ffffff"; c.LineWidth <- 0.5; c.BeginPath(); path.Call(borders) |> ignore; c.Stroke()
                c.StrokeStyle <- "#0f172a"; c.LineWidth <- 2.0; c.BeginPath(); path.Call(globe) |> ignore; c.Stroke()

            // The binding's transition API is v3-era (transition.each/on), which
            // D3 v7 does not support, so drive the transition via interop.
            let rec transition (i: int) =
                let tr = (As<Func<obj>> (d3?transition)).Invoke()
                (As<Action<int>> (tr?duration)).Invoke(1250)
                (As<Action<string, obj>> (tr?on)).Invoke(
                    "start", Action<obj>(fun _ -> title.Text(countries.[i]?name : string) |> ignore))
                (As<Action<string, obj>> (tr?tween)).Invoke(
                    "rotate", Func<obj, obj>(fun _ ->
                        let centroid = geoCentroid countries.[i]
                        let rotation = D3.Interpolate(projection.Rotate(), (-centroid.[0], -centroid.[1], 0.))
                        Action<float>(fun t ->
                            let (x, y, z) = rotation.Invoke t
                            projection.Rotate(x, y, z) |> ignore
                            draw countries.[i]) :> obj))
                let tr2 = (As<Func<obj>> (tr?transition)).Invoke()
                (As<Action<string, obj>> (tr2?on)).Invoke(
                    "end", Action<obj>(fun _ -> transition (i + 1)))

            transition 0
        }
        |> Async.Start

    [<SPAEntryPoint>]
    let Main () =
        Render (JS.Document.GetElementById "main")

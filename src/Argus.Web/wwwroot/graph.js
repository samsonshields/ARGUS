const instances = new WeakMap();

export function mount(element, graph, receiver) {
    destroy(element);
    const started = performance.now();
    if (!window.cytoscape) throw new Error("Local Cytoscape bundle did not load.");
    const cy = window.cytoscape({
        container: element, minZoom: 0.08, maxZoom: 5, wheelSensitivity: 0.2,
        elements: [
            ...graph.nodes.map(n => ({data: {id: n.id, label: n.label, type: n.type, degree: n.degree}})),
            ...graph.edges.map(e => ({data: {id: e.id, source: e.source, target: e.target, label: e.label,
                status: e.status, type: e.type, directed: e.directed}}))
        ],
        style: [
            {selector: "node", style: {label: "data(label)", "background-color": "#7ecbd8", color: "#e1e9f0",
                "font-size": 11, "text-valign": "bottom", "text-margin-y": 6, width: 28, height: 28,
                "text-wrap": "ellipsis", "text-max-width": 140, "border-width": 1, "border-color": "#b3e1e8"}},
            {selector: 'node[type="ORGANIZATION"]', style: {shape: "round-rectangle", "background-color": "#b0b7dc"}},
            {selector: 'node[type="LOCATION"]', style: {shape: "diamond", "background-color": "#8bc7a6"}},
            {selector: "edge", style: {width: 2, "curve-style": "bezier", "line-color": "#7ecbd8", "target-arrow-color": "#7ecbd8",
                "target-arrow-shape": e => e.data("directed") ? "triangle" : "none", "arrow-scale": 0.9}},
            {selector: 'edge[status="PROPOSED"]', style: {"line-style": "dashed", "line-color": "#e7bc6f", "target-arrow-color": "#e7bc6f"}},
            {selector: 'edge[type="MENTIONED_WITH"][status="APPROVED"]', style: {"line-style": "dotted", "line-color": "#91a9ba"}},
            {selector: ":selected", style: {"border-width": 4, "border-color": "#fff1ab", "line-color": "#fff1ab", "target-arrow-color": "#fff1ab"}},
            {selector: "edge:selected", style: {width: 4}},
            {selector: ".dimmed", style: {opacity: 0.18}},
            {selector: ".path", style: {"line-color": "#f2da86", "target-arrow-color": "#f2da86", "border-color": "#f2da86", "border-width": 4}},
            {selector: "edge.path", style: {width: 4}}
        ],
        layout: {name: "grid", padding: 40},
        motionBlur: false
    });
    instances.set(element, {cy, graph});
    cy.on("tap", "node", event => receiver.invokeMethodAsync("NodeSelected", event.target.id()).catch(() => {}));
    cy.on("tap", "edge", event => receiver.invokeMethodAsync("EdgeSelected", event.target.id()).catch(() => {}));
    cy.on("pan zoom", () => { element.dataset.pan = JSON.stringify(cy.pan()); element.dataset.zoom = String(cy.zoom()); });
    if (graph.nodes.length > 1 && graph.nodes.length <= 300)
        cy.layout({name: "cose", animate: false, randomize: false, numIter: 400, padding: 45, componentSpacing: 80}).run();
    element.dataset.nodes = String(graph.nodes.length);
    element.dataset.edges = String(graph.edges.length);
    element.dataset.renderMilliseconds = String(Math.round(performance.now() - started));
    element.dataset.graphReady = "true";
}

function state(element) {
    const current = instances.get(element);
    if (!current) throw new Error("The graph is not ready.");
    return current;
}
export function select(element, id) {
    const {cy} = state(element);
    const selected = cy.getElementById(id);
    cy.elements().unselect().removeClass("dimmed path");
    selected.select();
    const focus = selected.isNode() ? selected.closedNeighborhood() : selected.union(selected.connectedNodes());
    cy.fit(focus, 80);
    cy.center(selected);
}
export function action(element, command, id) {
    const {cy} = state(element);
    if (command === "clear") cy.elements().unselect().removeClass("dimmed path");
    if (command === "fit") { cy.elements().show().removeClass("dimmed path"); cy.fit(undefined, 40); }
    if (command === "zoomIn") cy.zoom({level: Math.min(cy.maxZoom(), cy.zoom() * 1.3), renderedPosition: {x: cy.width()/2, y: cy.height()/2}});
    if (command === "zoomOut") cy.zoom({level: Math.max(cy.minZoom(), cy.zoom() / 1.3), renderedPosition: {x: cy.width()/2, y: cy.height()/2}});
    if (command === "focus" && id) { cy.elements().hide(); cy.getElementById(id).show(); cy.fit(cy.getElementById(id), 100); }
    if (command === "expand" && id) {
        const hood = cy.getElementById(id).closedNeighborhood(); hood.show();
        const shown = cy.nodes(":visible"); cy.edges().filter(e => e.source().visible() && e.target().visible()).show();
        cy.elements().removeClass("dimmed"); cy.elements().not(hood).addClass("dimmed"); cy.fit(shown, 60);
    }
    element.dataset.zoom = String(cy.zoom());
    element.dataset.visibleNodes = String(cy.nodes(":visible").length);
}
export function highlightPath(element, path) {
    const {cy} = state(element);
    cy.elements().show().removeClass("path dimmed");
    delete element.dataset.pathNodes;
    if (!path.found) return;
    let selected = cy.collection();
    for (const id of [...path.nodeIds, ...path.edgeIds]) selected = selected.union(cy.getElementById(id));
    cy.elements().not(selected).addClass("dimmed"); selected.addClass("path"); cy.fit(selected, 70);
    element.dataset.pathNodes = String(path.nodeIds.length);
}
export function destroy(element) {
    const current = instances.get(element);
    if (current) { current.cy.destroy(); instances.delete(element); }
    if (element?.dataset) delete element.dataset.graphReady;
}

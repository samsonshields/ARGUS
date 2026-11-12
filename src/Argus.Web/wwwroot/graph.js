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
        // Shapes encode entity type; line style and color encode review state (see page legend).
        style: [
            {selector: "node", style: {label: "data(label)", "background-color": "transparent", color: "#fafafa",
                "font-family": "IBM Plex Sans, Arial, sans-serif", "font-size": 11, "font-weight": 500,
                "text-valign": "bottom", "text-margin-y": 6, width: 22, height: 22,
                "text-wrap": "wrap", "text-max-width": 110, "text-background-color": "#0b0b0c", "text-background-opacity": 0.92,
                "text-background-padding": 2, "text-background-shape": "round-rectangle", "border-width": 2, "border-color": "#8f8f94"}},
            {selector: 'node[type="PERSON"]', style: {width: 26, height: 26, "background-color": "#fafafa", "border-color": "#ffffff", "border-width": 1}},
            {selector: 'node[type="ORGANIZATION"]', style: {shape: "round-rectangle", width: 28, height: 28, "background-color": "#b4b4b4", "border-color": "#dcdcdc", "border-width": 1}},
            {selector: 'node[type="LOCATION"]', style: {shape: "diamond", width: 30, height: 30, "background-color": "#64a7ff", "border-color": "#a8cdff", "border-width": 1}},
            {selector: "edge", style: {width: 2, "curve-style": "bezier", "line-color": "#1ed760", "target-arrow-color": "#1ed760",
                "target-arrow-shape": e => e.data("directed") ? "triangle" : "none", "arrow-scale": 0.9}},
            {selector: 'edge[status="PROPOSED"]', style: {"line-style": "dashed", "line-dash-pattern": [6, 4], "line-color": "#f6b704", "target-arrow-color": "#f6b704", width: 1.6}},
            {selector: 'edge[type="MENTIONED_WITH"][status="APPROVED"]', style: {"line-style": "dotted", "line-color": "#8f8f94", "target-arrow-color": "#8f8f94"}},
            {selector: "node:selected", style: {"border-width": 3, "border-color": "#ffffff", "overlay-color": "#ffffff", "overlay-opacity": 0.08, "overlay-padding": 6}},
            {selector: "edge:selected", style: {width: 4, "line-color": "#ffffff", "target-arrow-color": "#ffffff"}},
            {selector: ".dimmed", style: {opacity: 0.16}},
            {selector: "node.path", style: {"border-color": "#ffffff", "border-width": 3}},
            {selector: "edge.path", style: {width: 4, "line-style": "solid", "line-color": "#ffffff", "target-arrow-color": "#ffffff"}}
        ],
        layout: {name: "grid", padding: 40},
        motionBlur: false
    });
    instances.set(element, {cy, graph});
    cy.on("tap", "node", event => receiver.invokeMethodAsync("NodeSelected", event.target.id()).catch(() => {}));
    cy.on("tap", "edge", event => receiver.invokeMethodAsync("EdgeSelected", event.target.id()).catch(() => {}));
    cy.on("pan zoom", () => { element.dataset.pan = JSON.stringify(cy.pan()); element.dataset.zoom = String(cy.zoom()); });
    if (graph.nodes.length > 1 && graph.nodes.length <= 300)
        cy.layout({name: "cose", animate: false, randomize: false, numIter: 400, padding: 50, componentSpacing: 100,
            nodeDimensionsIncludeLabels: true, nodeRepulsion: () => 9000, idealEdgeLength: () => 100, nodeOverlap: 20}).run();
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
    if (selected.isNode()) {
        // Center the node body, not its label-inclusive bounding box.
        const position = selected.position();
        cy.pan({x: cy.width() / 2 - position.x * cy.zoom(), y: cy.height() / 2 - position.y * cy.zoom()});
    } else cy.center(selected);
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

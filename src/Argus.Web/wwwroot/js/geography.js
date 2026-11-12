const maps = new WeakMap();
const safeLabel = value => { const node = document.createElement('span'); node.textContent = value; return node; };
export function render(element, data, dotnet, investigationId) {
    let state = maps.get(element);
    if (state && state.investigationId !== investigationId) { dispose(element); state = null; }
    if (!state) {
        const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
        const map = L.map(element, { worldCopyJump: true, minZoom: 1, maxZoom: 18, fadeAnimation: false,
            zoomAnimation: !reducedMotion, markerZoomAnimation: !reducedMotion });
        const focus = data.focus || { latitude: 20, longitude: 0, zoom: 2 };
        map.setView([focus.latitude, focus.longitude], focus.zoom);
        if (!data.focus && data.locations.length) map.fitBounds(data.locations.map(item => [item.latitude, item.longitude]), { maxZoom: 7, padding: [40, 40], animate: false });
        const Grid = L.GridLayer.extend({ createTile(coords) {
            const tile = document.createElement('div'); tile.className = 'coordinate-tile';
            const point = map.unproject(L.point(coords.x * 256 + 128, coords.y * 256 + 128), coords.z).wrap();
            tile.textContent = `${point.lat.toFixed(2)}°, ${point.lng.toFixed(2)}°`; return tile;
        }});
        new Grid({ attribution: 'Offline coordinate grid', noWrap: false }).addTo(map);
        state = { map, dotnet, markers: L.layerGroup().addTo(map), tileLayer: null, investigationId };
        maps.set(element, state);
        map.on('click', e => {
            const point = e.latlng.wrap();
            state.dotnet.invokeMethodAsync('PickCoordinates', Math.max(-90, Math.min(90, point.lat)), point.lng);
        });
        map.on('moveend zoomend', () => {
            const center = map.getCenter().wrap();
            element.dataset.latitude = center.lat.toFixed(6); element.dataset.longitude = center.lng.toFixed(6); element.dataset.zoom = map.getZoom();
        });
    }
    state.dotnet = dotnet; state.markers.clearLayers();
    const locations = new Map(data.locations.map(item => [item.id, item]));
    for (const item of data.locations) {
        const point = [item.latitude, item.longitude];
        if (item.precision === 'APPROXIMATE') L.circle(point, { radius: item.uncertaintyMeters, color: '#f6b704', fillOpacity: 0.12, weight: 2, interactive: false }).addTo(state.markers);
        const marker = L.circleMarker(point, { radius: 8, color: item.precision === 'APPROXIMATE' ? '#f6b704' : '#fafafa', fillOpacity: 0.9, weight: 2 });
        marker.bindTooltip(safeLabel(`${item.name} · ${item.precision}${item.uncertaintyMeters ? ` ±${item.uncertaintyMeters} m` : ''}`));
        marker.on('click', e => { L.DomEvent.stopPropagation(e); state.dotnet.invokeMethodAsync('SelectRecord', 'location', item.id); });
        marker.addTo(state.markers);
    }
    for (const item of data.events) {
        const location = locations.get(item.locationId); if (!location) continue;
        const marker = L.circleMarker([location.latitude, location.longitude], { radius: 15, color: '#64a7ff', fillOpacity: 0.2, weight: 3 });
        marker.bindTooltip(safeLabel(`${item.title} · event at ${location.name} (${location.precision})`));
        marker.on('click', e => { L.DomEvent.stopPropagation(e); state.dotnet.invokeMethodAsync('SelectRecord', 'event', item.id); });
        marker.addTo(state.markers);
    }
    element.dataset.locationCount = data.locations.length; element.dataset.eventCount = data.events.length;
    setTimeout(() => state.map.invalidateSize(), 0);
}
export function basemap(element, enabled) {
    const state = maps.get(element); if (!state) return;
    if (state.tileLayer) { state.map.removeLayer(state.tileLayer); state.tileLayer = null; }
    if (enabled) {
        // Browser caching and normal interactive viewport requests only; no prefetch or download API.
        state.tileLayer = L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
            minZoom: 1, maxZoom: 18, updateWhenIdle: true, keepBuffer: 1,
            attribution: '&copy; <a href="https://www.openstreetmap.org/copyright" target="_blank" rel="noopener noreferrer">OpenStreetMap contributors</a>'
        });
        state.tileLayer.on('tileerror', () => state.dotnet.invokeMethodAsync('TileError'));
        state.tileLayer.addTo(state.map); state.markers.bringToFront?.();
    }
}
export function focus(element) {
    const map = maps.get(element).map; const center = map.getCenter().wrap();
    return { latitude: Math.max(-90, Math.min(90, center.lat)), longitude: center.lng, zoom: map.getZoom() };
}
export function locate(element, latitude, longitude) { maps.get(element)?.map.panTo([latitude, longitude]); }
export function dispose(element) { const state = maps.get(element); if (state) { state.map.remove(); maps.delete(element); } }

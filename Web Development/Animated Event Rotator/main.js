/*
"cheer-alltime-top-donation": Object { amount: 0, name: "" }
"cheer-alltime-top-donator": Object { amount: 0, name: "" }
"cheer-count": Object { count: 0 }
"cheer-goal": Object { amount: 0 }
"cheer-latest": Object { name: "", amount: 0 }
"cheer-month": Object { amount: 0 }
"cheer-monthly-top-donation": Object { name: "", amount: 0 }
"cheer-monthly-top-donator": Object { name: "", amount: 0 }
"cheer-recent": Array []
"cheer-session": Object { amount: 0 }
"cheer-session-top-donation": Object { name: "", amount: 0 }
"cheer-session-top-donator": Object { name: "", amount: 0 }
"cheer-total": Object { amount: 0 }
"cheer-week": Object { amount: 0 }
"cheer-weekly-top-donation": Object { name: "", amount: 0 }
"cheer-weekly-top-donator": Object { name: "", amount: 0 }
"follower-goal": Object { amount: 7 }
"follower-latest": Object { name: "sweetc00kie" }
"follower-month": Object { count: 7 }
"follower-recent": Array(7) [ {…}, {…}, {…}, … ]
"follower-session": Object { count: 0 }
"follower-total": Object { count: 137 }
"follower-week": Object { count: 0 }
"raid-latest": Object { name: "tsukishikon", amount: 5 }
"raid-recent": Array(6) [ {…}, {…}, {…}, … ]
"subscriber-alltime-gifter": Object { name: "tsukishikon", amount: 49 }
"subscriber-gifted-latest": Object { name: "sassyservine17", amount: 1, message: "TsukiShikon gifted a Tier 1 sub to SassyServine17! They have given 52 Gift Subs in the channel!", … }
"subscriber-gifted-session": Object { count: 0 }
"subscriber-goal": Object { amount: 7 }
"subscriber-latest": Object { name: "sassyservine17", amount: 1, tier: "1000", … }
"subscriber-month": Object { count: 7 }
"subscriber-new-latest": Object { name: "sassyservine17", amount: 1, message: "TsukiShikon gifted a Tier 1 sub to SassyServine17! They have given 52 Gift Subs in the channel!" }
"subscriber-new-session": Object { count: 0 }
"subscriber-points": Object { amount: 8 }
"subscriber-recent": Array(7) [ {…}, {…}, {…}, … ]
"subscriber-resub-latest": Object { name: "iamspecious", amount: 21, message: "streaks" }
"subscriber-resub-session": Object { count: 0 }
"subscriber-session": Object { count: 0 }
"subscriber-total": Object { count: 8 }
"subscriber-week": Object { count: 4 }
*/

class SBWebSocket {
	wsAttemptConnect = true;
	wsConnectTimer = 0;
	wsAttemptCount = 0;
	loadedEmoteSets = 0;
	lastTick = null;
	ws = null;
	
	#_requestId = "";
	
	constructor(id="breaktimeStats") {
		this.#_requestId = id;
		
		this._boundTick = this.tick.bind(this);
		this._boundWsOpen = this.wsOpen.bind(this);
		this._boundWsError = this.wsError.bind(this);
		this._boundWsClose = this.wsClose.bind(this);
		this._boundWsMessage = this.wsMessage.bind(this);
		
		this._tickId = window.requestAnimationFrame(this._boundTick);
		
		this._messageQueue = [];
	}
	
	destroy() {
		window.cancelAnimationFrame(this._tickId);
		if(this.ws && this.ws.readyState != WebSocket.CLOSED) this.ws.close(1000);
	}

	tick(t) {
		if(!this.lastTick) this.lastTick = t;
		let dt = t - this.lastTick;
		this.lastTick = t;
		
		if(this.wsAttemptConnect) {
			this.wsConnectTimer -= dt;
			if(this.wsConnectTimer <= 0) this.connectWs();
		}
		
		this._tickId = window.requestAnimationFrame(this._boundTick);
	}

	connectWs() {
		this.wsAttemptConnect = false;
		this.wsConnectTimer = 10000;
		if(this.ws != null && this.ws.readyState != WebSocket.CLOSED) this.ws.close(1000);
		
		try {
			this.ws = new WebSocket("ws://localhost:8080/fireworks");
			this.ws.onopen = this._boundWsOpen;
			this.ws.onerror = this._boundWsError;
			this.ws.onclose = this._boundWsClose;
			this.ws.onmessage = this._boundWsMessage;
		} catch(e) {
			console.error("WebSocket connection failed due to unknown error!", e);
		}
	}

	wsOpen(e) {
		this.wsAttemptCount = 0;
		console.info("WebSocket connected!", e);
		this.ws.send(JSON.stringify({"request": "Subscribe", "id": this.#_requestId, "events": {"Twitch": ["Follow","Cheer","Raid","Sub","ReSub","GiftSub","GiftBomb"], "General": ["Custom"]}}));
		this.ws.send(JSON.stringify({'request': 'GetCredits', 'id': this.#_requestId+'_credits'}));
		
		if(this._messageQueue.length > 0) {
			for(let i = 0; i < this._messageQueue.length; i++) {
				let m = this._messageQueue[i];
				this.send(m[0], m[1]);
			}
			this._messageQueue = [];
		}
	}

	wsError(e) {
		console.warn("WebSocket errored!", e);
	}

	wsClose(e) {
		let code = e.code;
		if(e.code == 1000 || e.code == 1001) {
			console.info("WebSocket connection closed normally!", e);
		} else {
			console.error("WebSocket connection terminated due to error!", e);
			this.wsAttemptCount++;
			if(this.wsAttemptCount <= 10) {
				this.wsAttemptConnect = true;
				console.info("Attempting reconnect in "+((this.wsConnectTimer || 1)/1000).toFixed(2)+"s ...", "Attempt: "+this.wsAttemptCount);
			} else {
				console.error("(Re)connection attempt limit reached! Aborting.");
				if(_wsPromise != null) {
					_wsPromise[1]("Websocket could not connect - cannot fetch data!");
					_wsPromise = null;
				}
			}
		}
	}

	wsMessage(e) {
		if(!e.data) return;
		let data = null;
		try{data = JSON.parse(e.data);} catch(e){return;}
		console.debug("WebSocket received message!", e);
		
		if(data.id) {
			if(data.id == this.#_requestId) {
				console.info("WebSocket received relevant status message!");
				if(data.status != "ok") {
					console.warn("WebSocket received non-ok status message!", data);
					return;
				}
			} else if(data.id == this.#_requestId+'_credits') {
				console.info("WebSocket received credits data");
				this.handleCredits(data);
			}
		} else if(data.event) {
			let event = data.event || {}, source = event.source, type = event.type, payload = data.data;
			if(source == "Twitch") {
				if(type == "Follow") this.handleFollow(payload);
				if(type == "Raid") this.handleRaid(payload);
				if(type == "Cheer") this.handleCheer(payload);
				if(type == "Sub" || type == "ReSub") this.handleSub(payload);
				if(type == "GiftSub" || type == "GiftBomb") this.handleGift(payload);
			}
			else if((source == "None" || source == "General") && type == "Custom") this.handleCustomRequest(data);
			else return;
		}
	}
	
	send(args, data) {
		if(this.ws == null || this.ws.readyState != WebSocket.OPEN) {
			this._messageQueue.push([args, data]);
			return;
		}
		
		let request = {"request": "DoAction", "id": this.#_requestId, "action": {"id": "<REMOVED>"}, "args": {}};
		if(typeof(args) == "string") args = [args];
		if(typeof(data) == "string") data = [data];
		for(let i = 0; i < args.length; i++) {
			request.args[args[i]] = data[i] || null;
		}
		console.debug("Sending request:", request);
		try {this.ws.send(JSON.stringify(request));} catch(e) {console.warn("WebSocket could not send custom request!", e.message);}
	}
	
	handleFollow(data) {
		sessionData['follower-latest'].name = data.displayName;
		if(sessionData.goals.length > 0) {
			for(let i = 0; i < sessionData.goals.length; i++) {
				let g = sessionData.goals[i];
				if(g.type == "follower") g.current_amount++;
			}
		}
	}
	
	handleRaid(data) {
		let count = data.viewerCount ?? 1;
		let name = data.displayName ?? "???";
		let time = new Date().toISOString();
		
		let raidEvent = {name: name, amount: count, createdAt: time};
		sessionData['raid-latest'] = raidEvent;
		sessionData['raid-session'] = [name, ...sessionData['raid-session']];
		sessionData['raid-recent'] = [raidEvent, ...sessionData['raid-recent']];
	}
	
	handleCheer(data) {
		/*let amount = data.bits ?? 1;
		let name = data.displayName ?? "???";
		let message = data.message ?? "";
		let time = new Date().toISOString();
		
		sessionData['cheer-count'].amount++;
		sessionData['cheer-total'].amount += amount;
		sessionData['cheer-session'].amount += amount;
		sessionData['cheer-week'].amount += amount;
		sessionData['cheer-month'].amount += amount;
		
		let cheerEvent = {name: name, amount: amount, message: message};
		let cheerEventRecent = {...cheerEvent, createdAt: time};*/
		
		fetchSE().then(() => verifyBoxes());
	}
	
	handleSub(data) {
		fetchSE().then(() => verifyBoxes());
	}
	
	handleGift(data) {
		fetchSE().then(() => verifyBoxes());
	}
	
	handleCustomRequest(data) {
		console.info("WebSocket received update message!", data);
		let payload = data.data, action = payload.action, param = payload.data;
		switch(action) {
			case 'getGoals': fetchGoals(param); break;
			case 'addBotBan': sessionData['bots-banned'].amount++;
			default: return; break;
		}
	}
	
	handleCredits(data) {
		let raids = (data.events ?? {}).raided ?? false;
		if(raids) sessionData['raid-session'] = raids;
		let gifts = (data.events ?? {}).giftsubs ?? false;
		if(gifts) sessionData['subscriber-gifted-session-list'] = gifts;
		else gifts = (data.events ?? {}).giftbombs ?? false;
		if(gifts) sessionData['subscriber-gifted-session-list'] = gifts;
		let botBans = (data.custom ?? {}).botBans ?? false;
		if(botBans) sessionData['bots-banned'] = {amount: botBans.length};
		
		verifyBoxes();
	}
}

var inited = false;
var sessionData = {
	"cheer-alltime-top-donation": { amount: 0, name: "" },
	"cheer-alltime-top-donator": { amount: 0, name: "" },
	"cheer-count": { count: 0 },
	"cheer-goal": { amount: 0 },
	"cheer-latest": { name: "", amount: 0 },
	"cheer-month": { amount: 0 },
	"cheer-monthly-top-donation": { name: "", amount: 0 },
	"cheer-monthly-top-donator": { name: "", amount: 0 },
	"cheer-recent": [],
	"cheer-session": { amount: 0 },
	"cheer-session-top-donation": { name: "", amount: 0 },
	"cheer-session-top-donator": { name: "", amount: 0 },
	"cheer-total": { amount: 0 },
	"cheer-week": { amount: 0 },
	"cheer-weekly-top-donation": { name: "", amount: 0 },
	"cheer-weekly-top-donator": { name: "", amount: 0 },
	"follower-goal": { amount: 0 },
	"follower-latest": { name: "" },
	"follower-month": { count: 7 },
	"follower-recent": [],
	"follower-session": { count: 0 },
	"follower-total": { count: 0 },
	"follower-week": { count: 0 },
	"raid-latest": { name: "", amount: 0 },
	"raid-recent": [],
	"subscriber-alltime-gifter": { name: "", amount: 0 },
	"subscriber-gifted-latest": { name: "", amount: 0, message: "", sender: "", tier: "0"},
	"subscriber-gifted-session": { count: 0 },
	"subscriber-gifted-session-list": [],
	"subscriber-goal": { amount: 0 },
	"subscriber-latest": { name: "", amount: 0, tier: "0", sender: "", gifted: false, communityGifted: false, message: ""},
	"subscriber-month": { count: 0 },
	"subscriber-new-latest": { name: "", amount: 0, message: ""},
	"subscriber-new-session": { count: 0 },
	"subscriber-points": { amount: 0 },
	"subscriber-recent": [],
	"subscriber-resub-latest": { name: "", amount: 0, message: ""},
	"subscriber-resub-session": { count: 0 },
	"subscriber-session": { count: 0 },
	"subscriber-total": { count: 0 },
	"subscriber-week": { count: 0 },
	"raid-session": [],
	"bots-banned": { amount: 0 },
	"goals": []
};
var metadata = {id: "<REMOVED>", token: "<REMOVED>"};
var _ws = null;
var _wsPromise = null;
var _tempPromises = [];
var nextDirection = 0; //0 = L, 1 = R
var boxes = ['follows'];
var curBox = 'follows';
document.addEventListener('DOMContentLoaded', function(e) {
	_ws = new SBWebSocket();
	let p1 = fetchSE();
	_tempPromises.push(p1);
	let p2 = new Promise(function(res,rej) {_wsPromise = [res,rej];});
	_tempPromises.push(p2);
	_ws.send("action", "getTwitchGoals");
	
	Promise.all([p1, p2]).then(() => {console.log("All data loading done", sessionData); setup();}).catch((err) => console.warn("Could not fetch all data", err)).catch((err) => {
		showError();
	});
	
	//setup();
});

function setup() {
	try {
		verifyBoxes();
		
		let slash = document.getElementById('slash');
		slash.classList.add(nextDirection == 0 ? 'left' : 'right', 'start', 'play');
		slash.addEventListener('animationend', () => {
			let main = document.getElementById('main');
			
			let elem = getEventData();
			
			main.appendChild(elem);
		}, {once: true});
	} catch(err) {
		console.error("CAUGHT ERROR (in setup):", err);
		showError();
	}
}

function verifyBoxes() {
	boxes = ['follows'];
	let containsSubGoal = false;
	if(sessionData.goals && sessionData.goals.length > 0) {
		for(let i = 0; i < sessionData.goals.length; i++) {
			if(sessionData.goals[i].type == 'subscriber') {containsSubGoal = true; break;}
		}
	}
	if(sessionData['subscriber-total'].count > 0 || containsSubGoal) boxes.push('subs');
	if(sessionData['cheer-latest'].amount > 0) boxes.push('cheers');
	if(sessionData['subscriber-gifted-latest'].amount > 0 || (sessionData['subscriber-gifted-session'].count > 0 && sessionData['subscriber-gifted-session-list'].length > 0)) boxes.push('gifts');
	if(sessionData['raid-session'].length > 0) boxes.push('raids');
	if(sessionData['bots-banned'].amount > 0) boxes.push('bots');
}

function animDelay(e) {
	let cont = document.querySelector('.box.current');
	cont.classList.add('paused');
	setTimeout(() => {
		cont.classList.remove('paused');
		cont.addEventListener('animationend', switchSide, {once: true});
	}, 7500);
}

function switchSide(e) {
	try {
		nextDirection = (nextDirection + 1) % 2;
		let slash = document.getElementById('slash');
		let side = nextDirection == 0 ? 'left' : 'right';
		slash.classList.remove('start', 'left', 'right');
		slash.classList.add(side);
		
		let index = boxes.indexOf(curBox);
		if(index < 0) curBox = 'follows';
		else curBox = boxes[(index+1)%boxes.length];
		
		let old = document.querySelector('.box.current');
		old.classList.remove('current');
		old.children[0].addEventListener('animationend', () => old.remove());
		
		slash.addEventListener('animationend', () => {
			let main = document.getElementById('main');
			
			let elem = getEventData();
			
			main.appendChild(elem);
		}, {once: true});
	} catch(err) {
		console.error("CAUGHT ERROR (in switch):", err);
		showError();
	}
}

function getEventData() {
	let temp = document.getElementById('boxTemplate').content;
	let c = document.importNode(temp, true);
	let cont = c.children[0];
	let title = cont.children[0];
	let bodyBox = cont.children[1];
	let body = bodyBox.children[0];
	cont.classList.add(nextDirection == 0 ? 'left' : 'right', 'play', 'current');
	title.addEventListener('animationiteration', animDelay, {once: true});
	
	let name;
	switch(curBox) {
		case 'follows':
			name = sessionData['follower-latest'].name ?? '???';
			if(name == '') name == '???'
			
			title.children[0].children[0].innerText = 'Latest Follower';
			title.children[1].children[0].innerText = name;
			if(sessionData.goals && sessionData.goals.length > 0) {
				for(let i = 0; i < sessionData.goals.length; i++) {
					let g = sessionData.goals[i];
					if(g.type == 'follower') {
						body.insertAdjacentHTML('beforeend', `<div class="bold mask"><div class="text">Follower Goal</div></div>`);
						body.insertAdjacentHTML('beforeend', `<div class="light mask"><div class="text">${g.description}</div></div>`);
						body.insertAdjacentHTML('beforeend', `<div class="light mask"><div class="text"><b>${g.current_amount}/${g.target_amount}</b></div></div>`);
						break;
					}
				}
			}
			break;
		case 'subs':
			name = sessionData['subscriber-latest'].name ?? '???';
			if(name == '') name == '???'
			
			title.children[0].children[0].innerText = 'Latest Subscriber';
			title.children[1].children[0].innerText = name + getSubTier(sessionData['subscriber-latest'].tier ?? '0');
			if(sessionData.goals && sessionData.goals.length > 0) {
				for(let i = 0; i < sessionData.goals.length; i++) {
					let g = sessionData.goals[i];
					if(g.type == 'subscription') {
						body.insertAdjacentHTML('beforeend', `<div class="bold mask"><div class="text">Subscriber Goal</div></div>`);
						body.insertAdjacentHTML('beforeend', `<div class="light mask"><div class="text">${g.description}</div></div>`);
						body.insertAdjacentHTML('beforeend', `<div class="light mask"><div class="text"><b>${g.current_amount}/${g.target_amount}</b></div></div>`);
						break;
					}
				}
			}
			break;
		case 'cheers':
			name = sessionData['cheer-latest'].name ?? '???';
			if(name == '') name = 'Noone yet';
			
			title.children[0].children[0].innerText = 'Latest Cheerer';
			title.children[1].children[0].innerText = name + ' ('+sessionData['cheer-latest'].amount+' Bits)';
			if(sessionData['cheer-month'].amount > 0) {
				body.insertAdjacentHTML('beforeend', `<div class="bold mask"><div class="text">This month:</div></div>`);
				body.insertAdjacentHTML('beforeend', `<div class="light mask"><div class="text">Cheered <b>${sessionData['cheer-month'].amount}</b> Bits</div></div>`);
				body.insertAdjacentHTML('beforeend', '<br>');
				let highestName = sessionData['cheer-monthly-top-donator'].name ?? '???';
				if(highestName == '') highestName = '???';
				body.insertAdjacentHTML('beforeend', `<div class="bold mask"><div class="text">Highest cheerer:</div></div>`);
				body.insertAdjacentHTML('beforeend', `<div class="light mask"><div class="text">${highestName} (${sessionData['cheer-monthly-top-donator'].amount} Bits)</div></div>`);
			}
			break;
		case 'gifts':
			name = sessionData['subscriber-gifted-latest'].sender ?? '???';
			if(name == '') name = '???';
			
			title.children[0].children[0].innerText = 'Latest Gift Sub';
			title.children[1].children[0].innerText = name + ' (x'+sessionData['subscriber-gifted-latest'].amount+')';
			if(sessionData['subscriber-alltime-gifter'].amount > 0) {
				name = sessionData['subscriber-alltime-gifter'].name ?? '???';
				if(name == '') name = '???';
				
				body.insertAdjacentHTML('beforeend', `<div class="bold mask"><div class="text">Biggest Spender:</div></div>`);
				body.insertAdjacentHTML('beforeend', `<div class="light mask"><div class="text">${sessionData['subscriber-alltime-gifter'].name ?? '???'} (${sessionData['subscriber-alltime-gifter'].amount} Subs)</div></div>`);
				body.insertAdjacentHTML('beforeend', '<br>');
			}
			if(sessionData['subscriber-gifted-session'].count > 0 && sessionData['subscriber-gifted-session-list'] && sessionData['subscriber-gifted-session-list'].length > 0) {
				body.insertAdjacentHTML('beforeend', `<div class="bold mask"><div class="text">Gifties Today:</div></div>`);
				for(let i = 0; i < sessionData['subscriber-gifted-session'].count && i < sessionData['subscriber-gifted-session-list'].length && i < 5; i++) {
					body.insertAdjacentHTML('beforeend', `<div class="light mask"><div class="text">${sessionData['subscriber-gifted-session-list'][i]}</div></div><br>`);
				}
				if(sessionData['subscriber-gifted-session'].count > 5 && sessionData['subscriber-gifted-session-list'] && sessionData['subscriber-gifted-session-list'].length > 5) {
					body.insertAdjacentHTML('beforeend', `<div class="light mask"><div class="text">...and more!</div></div>`);
				}
			}
			break;
		case 'raids':
			name = sessionData['raid-latest'].name ?? '???';
			if(name == '') name = '???';
			
			title.children[0].children[0].innerText = 'Latest Raider';
			title.children[1].children[0].innerText = name + ' ('+sessionData['raid-latest'].amount+')';
			if(sessionData['raid-session'].length > 0) {
				let uniqueRaids = [name];
				for(let i = sessionData['raid-session'].length-1; i >= 0; i--) {
					let r = sessionData['raid-session'][i].toLowerCase();
					if(uniqueRaids.indexOf(r) > -1) continue;
					uniqueRaids.push(r);
				}
				uniqueRaids.shift();
				
				if(uniqueRaids.length > 0) {
					body.insertAdjacentHTML('beforeend', `<div class="bold mask"><div class="text">Today's Raiders:</div></div><br>`);
					for(let i = 0; i < uniqueRaids.length && i < 5; i++) {
						let name = uniqueRaids[i];
						let amount = tryFindRaidAmount(name);
						let text = amount > 0 ? (name + ' ('+amount+')') : name;
						body.insertAdjacentHTML('beforeend', `<div class="light mask"><div class="text">${text}</div></div><br>`);
					}
					if(uniqueRaids.length > 5) {
						body.insertAdjacentHTML('beforeend', `<div class="light mask"><div class="text">...and more!</div></div>`);
					}
				}
			}
			break;
		case 'bots':
			title.children[0].children[0].innerText = 'Bopped Bots';
			title.children[1].children[0].innerHTML = `<b>${sessionData['bots-banned'].amount}</b> erased today`;
			break;
	}
	
	return cont;
}

function tryFindRaidAmount(name) {
	name = name.toLowerCase();
	if(sessionData['raid-recent'].length <= 0) return 0;
	let amount = 0;
	for(let i = 0; i < sessionData['raid-recent'].length; i++) {
		let cname = sessionData['raid-recent'][i].name.toLowerCase();
		if(name == cname) {amount = sessionData['raid-recent'][i].amount ?? 0; break;}
	}
	return amount;
}

function getSubTier(val='0') {
	val = parseInt(val)??0;
	let tier = Math.floor(val / 1000);
	if(tier > 0) return ` (Tier ${tier})`;
	else return '';
}

async function fetchSE() {
	if(!metadata.id || !metadata.token) throw new Error("Missing SE authentication data");
	
	try {
		let response = await fetch("https://api.streamelements.com/kappa/v2/sessions/"+metadata.id, {
			method: "GET",
			headers: {
				"Authorization": "Bearer "+metadata.token
			}
		});
		let data = await response.json();
		sessionData = {...sessionData, ...data.data};
		return;
	} catch(err) {
		console.warn(err);
		throw new Error("Failed to fetch SE session data");
	};
}

function fetchGoals(raw) {
	try {
		let json = JSON.parse(raw);
		sessionData.goals = json.data || {};
		if(_wsPromise != null) {_wsPromise[0](); _wsPromise = null;}
	} catch(err) {
		console.error("Could not parse JSON goal data!", err);
		if(_wsPromise != null) {_wsPromise[1](); _wsPromise = null;}
	}
}

function showError() {
	let main = document.getElementById('main');
	main.style['display'] = 'none';
	let err = document.createElement('div');
	err.classList.add('error');
	err.innerText = "Sorry, brokey\nPlease fix";
	document.body.append(err);
}
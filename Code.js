// Инициализация конфигурации из Script Properties
var scriptProperties = PropertiesService.getScriptProperties();
var FIREBASE_DB_URL = scriptProperties.getProperty('FIREBASE_DB_URL');
var FIREBASE_SECRET = scriptProperties.getProperty('FIREBASE_SECRET');

// ВАЖНО: Если вы только что обновили код, запустите функцию setupSecrets() один раз
function setupSecrets() {
  var props = PropertiesService.getScriptProperties();
  props.setProperty('FIREBASE_DB_URL', 'https://bm-time-manager-default-rtdb.europe-west1.firebasedatabase.app');
  props.setProperty('FIREBASE_SECRET', 'xThKbWvljFGBPVxJxTKYHuLpyvzkKcnhkVCRx8pR');
  console.log("Секреты успешно сохранены в Script Properties!");
}

function pushFirebaseLog(message, data) {
  var url = FIREBASE_DB_URL + "/logs.json?auth=" + FIREBASE_SECRET;
  var payload = { timestamp: new Date().toISOString(), message: message, data: data || {} };
  var options = { method: "post", contentType: "application/json", payload: JSON.stringify(payload) };
  try { UrlFetchApp.fetch(url, options); } catch (err) {}
}

function doPost(e) {
  try {
    var params = e.parameter;
    var eventName = params['event'] || '';
    pushFirebaseLog("Received doPost", { eventName: eventName, params: params });
    
    var eventLower = eventName.toLowerCase();
    var auth = { accessToken: params['auth[access_token]'], clientEndpoint: params['auth[client_endpoint]'] };
    
    if (auth.accessToken) { updateFirebaseNode("/config/lastAuth", auth); }
    
    var groupId = params['data[FIELDS][ID]'] || params['data[PARAMS][ID]'];
    
    if (eventLower === 'onappinstall') {
      handleAppInstall(params);
    } else if (eventLower === 'onsonetgroupadd' || eventLower === 'onsonetgroupupdate') {
      if (groupId) syncGroupMembers(groupId, auth);
    } else if (eventLower === 'onsonetgroupdelete') {
      if (groupId) updateFirebaseNode("/groups/" + groupId + "/info/status", "deleted");
    }
    
    return ContentService.createTextOutput(JSON.stringify({status: 'success'})).setMimeType(ContentService.MimeType.JSON);
  } catch (error) {
    pushFirebaseLog("Error in doPost", { error: error.message });
    return ContentService.createTextOutput(JSON.stringify({status: 'error'})).setMimeType(ContentService.MimeType.JSON);
  }
}

function syncGroupMembers(groupId, auth) {
  try {
    var now = new Date().toISOString();
    
    // 1. Инфо о группе
    var groupInfoRes = UrlFetchApp.fetch(auth.clientEndpoint + "sonet_group.get?auth=" + auth.accessToken, {
      method: 'post', contentType: 'application/json', payload: JSON.stringify({ FILTER: { ID: groupId } })
    });
    var groupInfo = JSON.parse(groupInfoRes.getContentText()).result[0];
    if (!groupInfo) return;

    // 2. Все активные сотрудники портала
    var allUsersRes = UrlFetchApp.fetch(auth.clientEndpoint + "user.get?auth=" + auth.accessToken, {
      method: 'post', contentType: 'application/json', payload: JSON.stringify({ ACTIVE: "Y" })
    });
    var allUsers = JSON.parse(allUsersRes.getContentText()).result || [];

    // 3. Текущие участники группы
    var currentMembersRes = UrlFetchApp.fetch(auth.clientEndpoint + "sonet_group.user.get?auth=" + auth.accessToken, {
      method: 'post', contentType: 'application/json', payload: JSON.stringify({ ID: groupId })
    });
    var currentMembers = JSON.parse(currentMembersRes.getContentText()).result || [];
    var currentMemberIds = currentMembers.map(function(m) { return String(m.USER_ID); });

    // 4. Синхронизируем периоды для ВСЕХ сотрудников
    allUsers.forEach(function(user) {
      var uid = String(user.ID);
      var isInGroup = currentMemberIds.indexOf(uid) !== -1;
      manageMemberPeriod(groupId, uid, isInGroup ? "joined" : "left", now);
    });

    // 5. Обновляем справочник имен
    var membersData = {};
    allUsers.forEach(function(u) {
      membersData[String(u.ID)] = { fullName: (u.NAME + " " + u.LAST_NAME).trim() };
    });

    updateFirebaseNode("/groups/" + groupId + "/info", { id: groupId, name: groupInfo.NAME, archived: groupInfo.CLOSED === 'Y', status: 'active', updatedAt: now });
    updateFirebaseNode("/groups/" + groupId + "/members", membersData);
    
    pushFirebaseLog("Secure Sync v21 Done", { groupId: groupId });
    
  } catch (err) {
    pushFirebaseLog("Error in syncGroupMembers", { groupId: groupId, error: err.message });
  }
}

function manageMemberPeriod(groupId, userId, action, timestamp) {
  var path = "/groups/" + groupId + "/periods/" + userId;
  var res = UrlFetchApp.fetch(FIREBASE_DB_URL + path + ".json?auth=" + FIREBASE_SECRET);
  var periods = JSON.parse(res.getContentText()) || {};
  var periodKeys = Object.keys(periods);
  
  var openPeriodKey = null;
  for (var i = 0; i < periodKeys.length; i++) {
    if (!periods[periodKeys[i]].left) { openPeriodKey = periodKeys[i]; break; }
  }

  if (action === "joined") {
    if (!openPeriodKey) {
      UrlFetchApp.fetch(FIREBASE_DB_URL + path + ".json?auth=" + FIREBASE_SECRET, { 
        method: "post", contentType: "application/json", payload: JSON.stringify({ joined: timestamp }) 
      });
    }
  } else if (action === "left") {
    if (openPeriodKey) {
      UrlFetchApp.fetch(FIREBASE_DB_URL + path + "/" + openPeriodKey + ".json?auth=" + FIREBASE_SECRET, { 
        method: "patch", contentType: "application/json", payload: JSON.stringify({ left: timestamp }) 
      });
    }
  }
}

function updateFirebaseNode(path, data) {
  var url = FIREBASE_DB_URL + path + ".json?auth=" + FIREBASE_SECRET;
  var options = { method: data === null ? "delete" : "put", contentType: "application/json", payload: data === null ? null : JSON.stringify(data) };
  try { UrlFetchApp.fetch(url, options); } catch (err) {}
}

function handleAppInstall(params) {
  var accessToken = params['auth[access_token]'];
  var clientEndpoint = params['auth[client_endpoint]'];
  var myUrl = ScriptApp.getService().getUrl(); 
  var eventsToBind = ['onSonetGroupAdd', 'onSonetGroupUpdate', 'onSonetGroupDelete'];
  for (var i = 0; i < eventsToBind.length; i++) {
    var url = clientEndpoint + "event.bind?auth=" + accessToken;
    try { UrlFetchApp.fetch(url, { method: 'post', contentType: 'application/json', payload: JSON.stringify({ event: eventsToBind[i], handler: myUrl }) }); } catch(err) {}
  }
  
  var triggers = ScriptApp.getProjectTriggers();
  var found = false;
  for (var i = 0; i < triggers.length; i++) { if (triggers[i].getHandlerFunction() === 'scheduledSync') found = true; }
  if (!found) { ScriptApp.newTrigger('scheduledSync').timeBased().everyHours(1).create(); }
}

function scheduledSync() {
  var res = UrlFetchApp.fetch(FIREBASE_DB_URL + "/config/lastAuth.json?auth=" + FIREBASE_SECRET);
  var auth = JSON.parse(res.getContentText());
  if (!auth) return;
  var groupsRes = UrlFetchApp.fetch(auth.clientEndpoint + "sonet_group.get?auth=" + auth.accessToken);
  var groups = JSON.parse(groupsRes.getContentText()).result || [];
  groups.forEach(function(g) { syncGroupMembers(g.ID, auth); });
}

function doGet(e) {
  return ContentService.createTextOutput("Webhook v21 Secure Active. Using Script Properties for secrets.").setMimeType(ContentService.MimeType.TEXT);
}

// Инициализация конфигурации из Script Properties
var scriptProperties = PropertiesService.getScriptProperties();
var FIREBASE_DB_URL = scriptProperties.getProperty('FIREBASE_DB_URL');
var FIREBASE_SECRET = scriptProperties.getProperty('FIREBASE_SECRET');
var ADMIN_EMAIL = scriptProperties.getProperty('ADMIN_EMAIL') || 'gregory.yakovlev@gmail.com';
var DASHBOARD_URL = 'https://bm-time-manager.web.app';

// ВАЖНО: Если вы только что обновили код, запустите функцию setupSecrets() один раз
function setupSecrets() {
  var props = PropertiesService.getScriptProperties();
  props.setProperty('FIREBASE_DB_URL', 'https://bm-time-manager-default-rtdb.europe-west1.firebasedatabase.app');
  props.setProperty('FIREBASE_SECRET', 'xThKbWvljFGBPVxJxTKYHuLpyvzkKcnhkVCRx8pR');
  props.setProperty('ADMIN_EMAIL', 'gregory.yakovlev@gmail.com');
  props.setProperty('INVITE_CODE', 'BURO2026');
  
  // Записываем код в базу для проверки на фронтенде
  updateFirebaseNode("/config/inviteCode", props.getProperty('INVITE_CODE'));
  console.log("Секреты и Код приглашения успешно сохранены!");
}

function pushFirebaseLog(message, data) {
  var url = FIREBASE_DB_URL + "/logs.json?auth=" + FIREBASE_SECRET;
  var payload = { timestamp: new Date().toISOString(), message: message, data: data || {} };
  var options = { method: "post", contentType: "application/json", payload: JSON.stringify(payload) };
  try { UrlFetchApp.fetch(url, options); } catch (err) {}
}

function doGet(e) {
  var params = e.parameter;
  if (params.action === 'approve' && params.uid) {
    var url = FIREBASE_DB_URL + "/whitelist/" + params.uid + ".json?auth=" + FIREBASE_SECRET;
    UrlFetchApp.fetch(url, {
      method: 'put',
      contentType: 'application/json',
      payload: JSON.stringify({ approved: true, email: params.email, timestamp: new Date().toISOString() })
    });

    return HtmlService.createHtmlOutput(
      '<div style="font-family: sans-serif; text-align: center; padding: 50px;">' +
        '<h1 style="color: #fc4614;">Доступ одобрен!</h1>' +
        '<p>Сотрудник <b>' + params.email + '</b> теперь может пользоваться дашбордом.</p>' +
        '<a href="' + DASHBOARD_URL + '" style="display: inline-block; padding: 10px 20px; background: #fc4614; color: white; text-decoration: none; border-radius: 5px;">Перейти в BURO Manager</a>' +
      '</div>'
    );
  }
  return HtmlService.createHtmlOutput('BURO Manager Webhook is active.');
}

function doPost(e) {
  var contents;
  try {
    // 1. Проверяем, пришел ли JSON (от Дашборда)
    if (e.postData && e.postData.contents) {
      contents = JSON.parse(e.postData.contents);
      
      if (contents.action === 'request_access') {
        var approveUrl = ScriptApp.getService().getUrl() + "?action=approve&uid=" + contents.uid + "&email=" + encodeURIComponent(contents.email);
        
        MailApp.sendEmail({
          to: ADMIN_EMAIL,
          subject: "🔔 Запрос доступа: " + contents.name,
          htmlBody: 
            '<div style="font-family: sans-serif; padding: 20px; border: 1px solid #eee; border-radius: 10px;">' +
              '<h2 style="color: #fc4614;">Новый запрос в BURO Time Manager</h2>' +
              '<p><b>Сотрудник:</b> ' + contents.name + '</p>' +
              '<p><b>Email:</b> ' + contents.email + '</p>' +
              '<p>Чтобы дать доступ, нажмите кнопку ниже:</p>' +
              '<a href="' + approveUrl + '" style="display: inline-block; padding: 12px 24px; background: #fc4614; color: white; text-decoration: none; border-radius: 6px; font-weight: bold;">ОДОБРИТЬ ДОСТУП</a>' +
              '<p style="color: #666; font-size: 12px; margin-top: 20px;">Если вы не знаете этого человека, просто проигнорируйте письмо.</p>' +
            '</div>'
        });
        return ContentService.createTextOutput(JSON.stringify({ status: 'sent' })).setMimeType(ContentService.MimeType.JSON);
      }
    }

    // 2. Иначе обрабатываем как параметры от Битрикса
    var params = e.parameter;
    var eventName = params['event'] || '';
    pushFirebaseLog("Received Bitrix Event", { eventName: eventName });
    
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
    pushFirebaseLog("Error in doPost", { error: error.toString() });
    return ContentService.createTextOutput(JSON.stringify({status: 'error', message: error.toString()})).setMimeType(ContentService.MimeType.JSON);
  }
}

function syncGroupMembers(groupId, auth) {
  try {
    var now = new Date().toISOString();
    var groupInfoRes = UrlFetchApp.fetch(auth.clientEndpoint + "sonet_group.get?auth=" + auth.accessToken, {
      method: 'post', contentType: 'application/json', payload: JSON.stringify({ FILTER: { ID: groupId } })
    });
    var groupInfo = JSON.parse(groupInfoRes.getContentText()).result[0];
    if (!groupInfo) return;

    var allUsersRes = UrlFetchApp.fetch(auth.clientEndpoint + "user.get?auth=" + auth.accessToken, {
      method: 'post', contentType: 'application/json', payload: JSON.stringify({ ACTIVE: "Y" })
    });
    var allUsers = JSON.parse(allUsersRes.getContentText()).result || [];

    var currentMembersRes = UrlFetchApp.fetch(auth.clientEndpoint + "sonet_group.user.get?auth=" + auth.accessToken, {
      method: 'post', contentType: 'application/json', payload: JSON.stringify({ ID: groupId })
    });
    var currentMembers = JSON.parse(currentMembersRes.getContentText()).result || [];
    var currentMemberIds = currentMembers.map(function(m) { return String(m.USER_ID); });

    allUsers.forEach(function(user) {
      var uid = String(user.ID);
      var isInGroup = currentMemberIds.indexOf(uid) !== -1;
      manageMemberPeriod(groupId, uid, isInGroup ? "joined" : "left", now);
    });

    var membersData = {};
    allUsers.forEach(function(u) {
      membersData[String(u.ID)] = { fullName: (u.NAME + " " + (u.LAST_NAME || "")).trim() };
    });

    updateFirebaseNode("/groups/" + groupId + "/info", { 
      id: groupId, 
      name: groupInfo.NAME, 
      status: groupInfo.CLOSED === 'Y' ? 'archived' : 'active', 
      updatedAt: now 
    });
    updateFirebaseNode("/groups/" + groupId + "/members", membersData);
    
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
